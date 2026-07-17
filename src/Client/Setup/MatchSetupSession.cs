using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Setup;

/// <summary>
/// Connected-session owner of the replicated match setup. Lobby settings and
/// roster broadcasts land here, the frozen Welcome config keeps Rules honest
/// mid-match (a late joiner never saw the lobby), and each apply raises only
/// the events whose values actually transitioned.
/// </summary>
public sealed class MatchSetupSession : IMatchSetup, IDisposable
{
    private readonly List<MapOption> _mapOptions = [];
    private readonly List<LobbyMember> _members = [];
    private readonly List<SwapOffer> _swapOffers = [];
    private byte[] _rulesBytes;
    private bool _subscribed;

    public event Action? RulesChanged;
    public event Action? TeamsChanged;
    public event Action? SettingsChanged;
    public event Action? RosterChanged;
    public event Action? SwapOffersChanged;

    public bool HasServerState { get; private set; }
    public MatchConfig Rules { get; private set; } = new();
    public string MapId { get; private set; } = "";
    public string MapHash { get; private set; } = "";
    public IReadOnlyList<MapOption> MapOptions => _mapOptions;
    public string SettingsError { get; private set; } = "";
    public IReadOnlyList<LobbyMember> Members => _members;
    public IReadOnlyList<SwapOffer> SwapOffers => _swapOffers;

    public MatchSetupSession() => _rulesBytes = Rules.ToBytes();

    public MatchConfig CopyRules() => MatchConfig.FromBytes(_rulesBytes);

    public void Subscribe()
    {
        if (_subscribed)
            return;
        LobbySettingsMsg.Received += ApplySettings;
        LobbyStateMsg.Received += ApplyLobbyState;
        WelcomeMsg.Received += ApplyWelcome;
        _subscribed = true;
    }

    public void Clear()
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

    public void Dispose()
    {
        if (_subscribed)
        {
            LobbySettingsMsg.Received -= ApplySettings;
            LobbyStateMsg.Received -= ApplyLobbyState;
            WelcomeMsg.Received -= ApplyWelcome;
            _subscribed = false;
        }
        Clear();
    }

    internal void ApplySettings(LobbySettingsMsg message)
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
            _mapOptions.Add(new MapOption(message.MapIds[i], message.MapNames[i]));
        ApplyRules(rules, settingsChanged);
    }

    internal void ApplyLobbyState(LobbyStateMsg message)
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
            offers[i] = new SwapOffer(message.SwapFrom[i], message.SwapTo[i]);
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
    internal void ApplyWelcome(WelcomeMsg message)
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
            if (_mapOptions[i].Id != mapIds[i] || _mapOptions[i].Name != mapNames[i])
                return true;
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
