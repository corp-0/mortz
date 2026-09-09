using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Admin;
using Mortz.Server.Admission;
using Mortz.Server.Chat;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Pings;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Mortz.Server.Settings;
using Mortz.Server.Wins;
using Serilog;

namespace Mortz.Server;

/// <summary>The whole dedicated server: Connect, Disconnect, Receive, Inputs, Advance, Dispose.</summary>
public sealed class GameServer : IDisposable, IHandle<Player, PhaseReadyMsg>
{
    private readonly ServerBoot _boot;
    private readonly string _applicationVersion;
    private readonly ReadyLink _link;
    private readonly ILogger _log;
    private readonly IMatchObserver _matchObserver;
    private readonly ServerClock _clock;
    private readonly PhaseTransitionCoordinator _host;
    private readonly Roster _roster;
    private readonly NetRouter<Player> _router = new();
    private readonly HashSet<ushort> _undispatched = [];
    private readonly FeatureScope _scope;
    private readonly SettingsService _settings;
    private readonly AdminService _admin;
    private readonly ChatService _chat;
    private readonly MatchDependencies _matchDependencies;

    private bool _disposed;

    public GameServer(ServerBoot boot, IServerTransport transport, IMapSource maps, ILogger log,
        IMatchObserver observer, IMatchControl control, string applicationVersion)
    {
        _boot = boot;
        _applicationVersion = applicationVersion;
        _link = new ReadyLink(transport);
        _log = log;
        _matchObserver = observer;

        _host = new PhaseTransitionCoordinator(generation: 1);
        var slots = new ServerStateKeys(_host.Generation);
        _scope = new FeatureScope(_router.Add, _router.Remove);
        _scope.Register(this);
        _clock = new ServerClock();
        _roster = new Roster(slots);

        _settings = new SettingsService(boot, maps, _link, log);
        _admin = new AdminService(slots, _link, _clock, _host, log, boot.AdminPassword);
        _chat = new ChatService(slots, _link, _clock, new Random(boot.Seed));
        TypingService typing = new(slots, _link);
        WinsService wins = new(slots, _roster, _link, log);
        PingService pings = new(_link);
        EndMatchService endMatch = new(_admin, _chat, _host, _host);
        _scope.Register(_settings);
        _scope.Register(_admin);
        _scope.Register(_chat);
        _scope.Register(typing);
        _scope.Register(wins);
        _scope.Register(pings);
        _scope.Register(endMatch);
        slots.Seal();
        _matchDependencies = new MatchDependencies
        {
            Settings = _settings,
            Wins = wins,
            Roster = _roster,
            Link = _link,
            Log = _log,
            Observer = _matchObserver,
            Control = control,
            Clock = _clock,
            NetStats = _boot.NetStats,
            AllowJoinInProgress = _boot.AllowJoinInProgress,
        };

        _host.OpenInitial(LobbyPhase.Open(
            _roster, _settings, _admin, _chat, _link, log, _host));
        _scope.Own(_host.Dispose);
        _scope.OwnState(() =>
        {
            foreach (Player player in _roster)
            {
                player.Close(_host.Kind);
            }
        }, slots.Describe);
        _scope.Start();
        BindPhase();
    }

    public ServerPhaseKind Phase => _host.Kind;
    public int Generation => _host.Generation;

    public int PlayerCount => _roster.Count;

    public void Connect(AdmittedPlayer admitted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _link.BeginLoading(admitted.PeerId, _host.Generation, _clock.Ms);
        Player player = _roster.Join(admitted.PeerId, admitted.Name, admitted.Skin, admitted.Account);
        _host.OpenPhaseKeys(player);
        foreach (IObservePlayers feature in Live<IObservePlayers>())
        {
            feature.PlayerJoined(player);
        }

        _host.PlayerJoined(player);
        _matchObserver.PlayerJoined(player, _host.Kind);
        Execute(_host.Load(player));
    }

    public void Disconnect(int peerId)
    {
        if (_disposed)
            return;
        // Out of the roster before the fan-out, so tallies and broadcasts exclude
        // them; their name and state stay readable until Close below.
        if (_roster.Leave(peerId) is not Player player)
            return;
        _link.Remove(peerId);
        _host.PlayerLeft(player);
        foreach (IObservePlayers feature in Live<IObservePlayers>().Reverse())
        {
            feature.PlayerLeft(player);
        }

        _matchObserver.PlayerLeft(player, _host.Kind);
        player.Close(_host.Kind);
        Execute(_host.PlayerDisconnected(peerId, _roster.Count));
    }

    public void Receive(int peerId, ushort msgId, byte[] payload)
    {
        if (_disposed || _roster.Find(peerId) is not Player player)
            return;
        if (_router.Dispatch(msgId, player, payload))
            return;
        // A client can legitimately race a phase change, so this is noise, not an error.
        if (_undispatched.Add(msgId))
            _log.Information("no handler for {MessageName}", NetRegistry.NameOf(msgId));
    }

    public void Handle(Player player, in PhaseReadyMsg message)
    {
        if (!_link.Ready(player.PeerId, message.Generation))
            return;
        Execute(_host.Ready(player, message.Generation, _roster.Count));
    }

    public void Inputs(int peerId, byte[] packet)
    {
        if (!_host.InputsAllowed)
            return;
        if (_disposed || _roster.Find(peerId) is not Player player)
            return;
        _host.Inputs(player, packet);
    }

    public void Advance(ServerTime time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Ms = time.Ms;
        _link.DisconnectExpired(time.Ms);
        foreach (IAdvance feature in Live<IAdvance>())
        {
            feature.Advance(time);
        }

        Execute(_host.Advance(time));
    }

    public IEnumerable<Player> PublicationPlayers => _roster;

    public ServerInfo Describe() => new(
        _boot.Name,
        _settings.ModeId,
        _settings.Map.DisplayName,
        _roster.Count,
        NetConfig.MAX_PLAYERS,
        Phase == ServerPhaseKind.LOBBY,
        _boot.AllowJoinInProgress,
        _boot.GamePort,
        NetConfig.PROTOCOL_VERSION,
        NetRegistry.SCHEMA_HASH,
        NetConfig.GAME_APP_ID,
        _applicationVersion);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _router.Clear();
        _scope.Dispose();
    }

    private void StartMatch(IReadOnlyList<SeatAssignment> seats)
    {
        _log.Information("all {Players} player(s) ready, starting match", seats.Count);
        EnterPhase(MatchPhase.Open(seats, _host.NextGeneration, _matchDependencies));
    }

    private void ReturnToLobby()
    {
        if (_host.Kind != ServerPhaseKind.MATCH)
            return;
        _log.Information("back to lobby ({Players} player(s))", _roster.Count);
        EnterPhase(LobbyPhase.Open(_roster, _settings, _admin, _chat, _link, _log, _host));
    }

    /// <summary>Open the next phase for everyone already connected.</summary>
    private void EnterPhase(ServerPhase next)
    {
        Player[] players = [.. _roster];
        int generation = _host.NextGeneration;
        foreach (Player player in players)
        {
            _link.BeginLoading(player.PeerId, generation, _clock.Ms);
        }

        IReadOnlyList<PhaseHostAction> loads = _host.TransitionTo(next, players);
        BindPhase();
        Execute(loads);
        foreach (IObservePhase feature in Live<IObservePhase>())
        {
            feature.PhaseChanged(next.Kind);
        }

        _matchObserver.PhaseChanged(next.Kind);
    }

    private void Execute(PhaseHostAction? action)
    {
        if (action == null)
            return;
        switch (action)
        {
            case PhaseHostAction.SendLobbyLoad load:
                _link.Send(load.Player.PeerId, new LobbyLoadMsg(load.Generation));
                break;
            case PhaseHostAction.SendMatchLoad load:
                _host.LoadMatch(load.Player, load.Generation, load.Initial);
                break;
            case PhaseHostAction.SyncJip sync:
                foreach (ISyncJip feature in _host.Features.Implementing<ISyncJip>())
                {
                    feature.Sync(sync.Player);
                }

                break;
            case PhaseHostAction.SendMatchStart start:
                _link.Send(start.Player.PeerId, new MatchStartMsg(start.Generation));
                break;
            case PhaseHostAction.BroadcastMatchStart start:
                _link.Broadcast(new MatchStartMsg(start.Generation));
                _log.Information("all transition players loaded; match starting");
                break;
            case PhaseHostAction.EnterLobby:
                ReturnToLobby();
                break;
            case PhaseHostAction.EnterMatch start:
                StartMatch(start.Seats);
                break;
        }
    }

    private void Execute(IReadOnlyList<PhaseHostAction> actions)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            Execute(actions[i]);
        }
    }

    private IEnumerable<T> Live<T>() =>
        _scope.Implementing<T>().Concat(_host.Features.Implementing<T>());

    public string DescribeFeatures() => _scope.Describe() + "; phase: " + _host.Features.Describe();

    private void BindPhase()
    {
        _router.MatchGeneration = _host.Generation;
        _host.Features.Bind(_router.Add, _router.Remove);
        _log.Information("{Routes}", _router.Describe());
    }
}
