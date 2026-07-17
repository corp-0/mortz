using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Setup;

/// <summary>
/// Connected-session owner of the canonical match setup as the server
/// currently knows it: parsed rules, selected map and catalog, and the lobby
/// roster with team assignments. UI reads current values here and re-renders
/// on the events instead of tracking the wire messages itself; events fire on
/// actual value transitions, never once per message. The frozen Welcome config
/// keeps Rules honest mid-match (a late joiner never saw the lobby), and every
/// lobby broadcast triggers a settings request, closing the one-shot snapshot
/// race on lobby entry.
/// </summary>
public partial class MatchSetup : SessionScopedNode
{
    private readonly List<MapOption> _mapOptions = [];
    private readonly List<LobbyMember> _members = [];
    private readonly List<SwapOffer> _swapOffers = [];
    private byte[] _rulesBytes;

    /// <summary>Any rule value changed.</summary>
    public event Action? RulesChanged;

    /// <summary>The Teams rule toggled or a lobby team assignment moved.</summary>
    public event Action? TeamsChanged;

    /// <summary>The selected map, catalog, rules, or settings error changed.</summary>
    public event Action? SettingsChanged;

    /// <summary>Lobby membership, a name, or a ready state changed.</summary>
    public event Action? RosterChanged;

    /// <summary>A pending swap offer appeared, resolved, or expired.</summary>
    public event Action? SwapOffersChanged;

    /// <summary>False until the first valid server settings arrive.</summary>
    public bool HasServerState { get; private set; }

    /// <summary>Canonical rules; treat as read-only. Editors mutate a CopyRules().</summary>
    public MatchConfig Rules { get; private set; } = new();

    public string MapId { get; private set; } = "";
    public string MapHash { get; private set; } = "";
    public IReadOnlyList<MapOption> MapOptions => _mapOptions;

    /// <summary>Empty while the last received server settings were valid.</summary>
    public string SettingsError { get; private set; } = "";

    public IReadOnlyList<LobbyMember> Members => _members;
    public IReadOnlyList<SwapOffer> SwapOffers => _swapOffers;

    public MatchSetup() => _rulesBytes = Rules.ToBytes();

    public MatchConfig CopyRules() => MatchConfig.FromBytes(_rulesBytes);

    protected override void OnServiceReady()
    {
        LobbySettingsMsg.Received += ApplySettings;
        LobbyStateMsg.Received += OnLobbyState;
        WelcomeMsg.Received += ApplyWelcome;
    }

    protected override void OnServiceExit()
    {
        LobbySettingsMsg.Received -= ApplySettings;
        LobbyStateMsg.Received -= OnLobbyState;
        WelcomeMsg.Received -= ApplyWelcome;
        Clear();
    }

    protected override void OnSessionBoundary() => Clear();

    internal void ApplySettingsForTest(LobbySettingsMsg message) => ApplySettings(message);
    internal void ApplyLobbyStateForTest(LobbyStateMsg message) => ApplyLobbyState(message);

    private void Clear()
    {
        bool hadState = HasServerState || SettingsError != "" || MapId != "" ||
                        _mapOptions.Count > 0;
        HasServerState = false;
        SettingsError = "";
        MapId = "";
        MapHash = "";
        _mapOptions.Clear();
        ApplyRules(new MatchConfig(), raiseSettings: hadState);
        ApplyMembers([]);
        ApplyOffers([]);
    }

    private void OnLobbyState(LobbyStateMsg message)
    {
        ApplyLobbyState(message);
        new LobbySettingsRequestMsg().SendToServer();
    }

    private void ApplySettings(LobbySettingsMsg message)
    {
        if (message.MapIds.Length != message.MapNames.Length ||
            message.MapIds.Length > NetConfig.MAX_LOBBY_MAPS)
        {
            SetError("Server sent an invalid map catalog.");
            return;
        }

        MatchConfig rules;
        try
        {
            rules = MatchConfig.FromBytes(message.Config);
        }
        catch (IOException)
        {
            SetError("Server sent invalid match settings.");
            return;
        }

        bool settingsChanged = !HasServerState || SettingsError != "" ||
                               MapId != message.MapId || MapHash != message.MapHash ||
                               CatalogChanged(message.MapIds, message.MapNames);
        HasServerState = true;
        SettingsError = "";
        MapId = message.MapId;
        MapHash = message.MapHash;
        _mapOptions.Clear();
        for (int i = 0; i < message.MapIds.Length; i++)
        {
            _mapOptions.Add(new MapOption(message.MapIds[i], message.MapNames[i]));
        }
        ApplyRules(rules, settingsChanged);
    }

    private void ApplyLobbyState(LobbyStateMsg message)
    {
        int count = Math.Min(message.PeerIds.Length,
            Math.Min(message.Names.Length, message.ReadyFlags.Length));
        LobbyMember[] members = new LobbyMember[count];
        for (int i = 0; i < count; i++)
        {
            byte team = i < message.Teams.Length ? message.Teams[i] : (byte)0;
            members[i] = new LobbyMember(message.PeerIds[i], message.Names[i],
                message.ReadyFlags[i] != 0, team);
        }
        ApplyMembers(members);

        int offerCount = Math.Min(message.SwapFrom.Length, message.SwapTo.Length);
        SwapOffer[] offers = new SwapOffer[offerCount];
        for (int i = 0; i < offerCount; i++)
        {
            offers[i] = new SwapOffer(message.SwapFrom[i], message.SwapTo[i]);
        }
        ApplyOffers(offers);
    }

    private void ApplyOffers(IReadOnlyList<SwapOffer> offers)
    {
        if (offers.SequenceEqual(_swapOffers))
            return;
        _swapOffers.Clear();
        _swapOffers.AddRange(offers);
        SwapOffersChanged?.Invoke();
    }

    /// <summary>Mid-match canonical rules and map for players who never saw
    /// the lobby broadcast; the catalog stays whatever it was.</summary>
    private void ApplyWelcome(WelcomeMsg message)
    {
        MatchConfig rules;
        try
        {
            rules = MatchConfig.FromBytes(message.Config);
        }
        catch (IOException)
        {
            return; // the session controller rejects the welcome itself
        }

        bool settingsChanged = !HasServerState ||
                               MapId != message.MapId || MapHash != message.MapHash;
        HasServerState = true;
        MapId = message.MapId;
        MapHash = message.MapHash;
        ApplyRules(rules, settingsChanged);
    }

    private void ApplyRules(MatchConfig rules, bool raiseSettings)
    {
        byte[] bytes = rules.ToBytes();
        bool rulesChanged = !bytes.AsSpan().SequenceEqual(_rulesBytes);
        bool teamsToggled = Rules.Teams != rules.Teams;
        Rules = rules;
        _rulesBytes = bytes;
        if (rulesChanged)
            RulesChanged?.Invoke();
        if (teamsToggled)
            TeamsChanged?.Invoke();
        if (raiseSettings || rulesChanged)
            SettingsChanged?.Invoke();
    }

    private void ApplyMembers(IReadOnlyList<LobbyMember> members)
    {
        bool rosterChanged = !members.Select(WithoutTeam).SequenceEqual(
            _members.Select(WithoutTeam));
        bool teamsMoved = !Assignments(members).SequenceEqual(Assignments(_members));
        if (!rosterChanged && !teamsMoved)
            return;
        _members.Clear();
        _members.AddRange(members);
        if (rosterChanged)
            RosterChanged?.Invoke();
        if (teamsMoved)
            TeamsChanged?.Invoke();
    }

    private static LobbyMember WithoutTeam(LobbyMember member) => member with { Team = 0 };

    /// <summary>Only real assignments count, so joins and leaves in a
    /// teamless lobby never read as team movement.</summary>
    private static IEnumerable<(long PeerId, byte Team)> Assignments(
        IEnumerable<LobbyMember> members) =>
        members.Where(member => member.Team != 0)
            .Select(member => (member.PeerId, member.Team));

    private bool CatalogChanged(string[] mapIds, string[] mapNames)
    {
        if (_mapOptions.Count != mapIds.Length)
            return true;
        for (int i = 0; i < mapIds.Length; i++)
        {
            if (_mapOptions[i].Id != mapIds[i] || _mapOptions[i].Name != mapNames[i])
                return true;
        }
        return false;
    }

    private void SetError(string error)
    {
        if (SettingsError == error)
            return;
        SettingsError = error;
        SettingsChanged?.Invoke();
    }
}
