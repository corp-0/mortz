using System.Collections;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Players;

/// <summary>Everyone this client has heard of on the current connection, in
/// ascending peer id. Any stream may materialize a player first; the full
/// tables (lobby state and match roster broadcasts) confirm identity and are
/// the only thing that retires one.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientPlayers : Node,
    IReadOnlyCollection<ClientPlayer>,
    IHandle<RosterMsg>,
    IHandle<LobbyStateMsg>,
    IHandle<TypingStateMsg>,
    IHandle<PlayerModifiersMsg>
{
    private static readonly ILogger _log = MortzLog.For("client");
    private static int _nextGeneration;

    private readonly SortedDictionary<int, ClientPlayer> _players = [];
    private int _matchGeneration;

    public SessionStateKeys SessionKeys { get; } = new(++_nextGeneration);

    private MatchStateKeys? _matchKeys;
    private MatchConfig? _matchConfig;
    private PlayerStats? _matchBaseStats;

    /// <summary>Keys for the open match. The session controller opens a match
    /// before mounting its scene; match cells then live until the next open or
    /// the end of the session, because teardown is deferred (QueueFree) and
    /// messages keep landing on dying handlers for the rest of the frame.</summary>
    public MatchStateKeys MatchKeys =>
        _matchKeys ?? throw new InvalidOperationException("No match is open.");

    /// <summary>Latest match roster broadcast, kept for the slot-id wire path
    /// (snapshots address players by slot).</summary>
    public RosterSnapshot Table { get; private set; } = RosterSnapshot.Empty;

    public event Action<ClientPlayer>? PlayerJoined;
    public event Action<ClientPlayer>? PlayerLeft;
    public event Action<ClientPlayer>? MatchStatsChanged;

    /// <summary>Membership or any player's identity changed.</summary>
    public event Action? Changed;

    public int Count => _players.Count;

    public ClientPlayer? Find(int peerId) => _players.GetValueOrDefault(peerId);

    /// <summary>Display lookup that never materializes a player, for ids that
    /// may never confirm (0 for "nobody", stale event actors).</summary>
    public string NameOf(int peerId) =>
        Find(peerId)?.Name ?? ClientPlayer.UnknownName(peerId);

    /// <summary>Null when the player is unknown or teams are off.</summary>
    public Team? TeamOf(int peerId) => Find(peerId)?.Team;

    /// <summary>The player for this peer, created on first reference. Every
    /// stream funnels through here, so ordering races cannot drop state:
    /// whoever arrives first creates the player the others enrich.</summary>
    public ClientPlayer GetOrCreate(int peerId)
    {
        if (_players.TryGetValue(peerId, out ClientPlayer? player))
            return player;
        player = new ClientPlayer(peerId, SessionKeys, _matchKeys, _matchBaseStats);
        _players[peerId] = player;
        PlayerJoined?.Invoke(player);
        return player;
    }

    public IEnumerator<ClientPlayer> GetEnumerator() => _players.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void OpenMatch(MatchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        MatchStateKeys keys = new(++_matchGeneration);
        _matchKeys = keys;
        _matchConfig = config;
        _matchBaseStats = PlayerStats.Resolve(config);
        foreach (ClientPlayer player in _players.Values)
        {
            player.OpenMatch(keys, _matchBaseStats);
        }
    }

    /// <summary>Makes the snapshot's player presentation values available through
    /// <see cref="ClientPlayer.Match"/>.</summary>
    public void ApplySnapshot(MatchSnapshot snapshot)
    {
        foreach (ReplicatedPlayer replicated in snapshot.Players)
        {
            ClientPlayer player = GetOrCreate(replicated.Simulation.PeerId);
            player.Match?.ApplyPresentation(snapshot.Tick, replicated.Presentation);
        }
    }

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public void Handle(in RosterMsg message)
    {
        // Row decoding already validated each entry; this rejects duplicate
        // peers or slots across the table.
        if (!RosterSnapshot.TryFrom(message.Entries, out RosterSnapshot? snapshot))
            return;
        Table = snapshot;
        bool changed = false;
        foreach (RosterEntry entry in snapshot.Entries)
        {
            changed |= GetOrCreate(entry.PeerId).Apply(entry);
        }
        changed |= RetireAbsent(peerId => snapshot.TryFind(peerId, out _));
        if (changed)
            Changed?.Invoke();
    }

    public void Handle(in LobbyStateMsg message)
    {
        bool changed = false;
        foreach (LobbyMember member in message.Members)
        {
            changed |= GetOrCreate(member.PeerId).Apply(member);
        }
        HashSet<int> present = [.. message.Members.Select(member => member.PeerId)];
        changed |= RetireAbsent(present.Contains);
        if (changed)
            Changed?.Invoke();
    }

    public void Handle(in TypingStateMsg message)
    {
        GetOrCreate(message.PeerId).ApplyTyping(message.IsTyping);
    }

    public void Handle(in PlayerModifiersMsg message)
    {
        if (_matchConfig == null)
            return;
        List<StatsModifier> modifiers;
        PlayerStats stats;
        try
        {
            modifiers = ModifierWire.Deserialize(message.Modifiers);
            stats = StatsPipeline.Resolve(_matchConfig, modifiers);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _log.Error(exception, "dropped malformed modifiers for peer {PeerId}", message.PeerId);
            return;
        }

        ClientPlayer player = GetOrCreate(message.PeerId);
        if (player.Match == null)
            return;
        player.Match.ApplyModifiers(stats, modifiers);
        MatchStatsChanged?.Invoke(player);
    }

    private bool RetireAbsent(Func<int, bool> present)
    {
        List<ClientPlayer>? gone = null;
        foreach (ClientPlayer player in _players.Values)
        {
            // Unconfirmed players are spared: a faster stream referenced them
            // and their confirming broadcast is still in flight.
            if (!player.Known || present(player.PeerId))
                continue;
            (gone ??= []).Add(player);
        }
        if (gone == null)
            return false;
        // Subscribers get their last look at the player's state, then it goes.
        foreach (ClientPlayer player in gone)
        {
            _players.Remove(player.PeerId);
            PlayerLeft?.Invoke(player);
            player.Retire();
        }
        return true;
    }
}
