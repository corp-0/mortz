using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Setup;

/// <summary>
/// The canonical match setup as the server knows it: parsed rules, selected
/// map and catalog, lobby roster with teams. UI reads values here and
/// re-renders on the events; events fire on actual value transitions, never
/// once per message. The Welcome config keeps Rules honest for a late joiner,
/// and every lobby broadcast triggers a settings request, closing the
/// snapshot race on lobby entry.
/// </summary>
public partial class MatchSetup : Node
{
    private readonly List<ContentOption> _mapOptions = [];
    private readonly List<ContentOption> _modeOptions = [];
    private readonly List<LobbyMember> _members = [];
    private readonly List<SwapOffer> _swapOffers = [];
    private byte[] _configBytes;

    public event Action? ConfigChanged;

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

    public MatchConfig Config { get; private set; } = new();

    public string MapId { get; private set; } = "";
    public string MapHash { get; private set; } = "";
    public IReadOnlyList<ContentOption> MapOptions => _mapOptions;

    /// <summary>The mode the rules currently match, "" when they match none.</summary>
    public string ModeId { get; private set; } = "";

    public IReadOnlyList<ContentOption> ModeOptions => _modeOptions;

    /// <summary>Empty while the last received server settings were valid.</summary>
    public string SettingsError { get; private set; } = "";

    public IReadOnlyList<LobbyMember> Members => _members;
    public IReadOnlyList<SwapOffer> SwapOffers => _swapOffers;

    public MatchSetup() => _configBytes = Config.ToBytes();

    public MatchConfig CopyConfig() => MatchConfig.FromBytes(_configBytes);

    public override void _Ready()
    {
        LobbySettingsMsg.Received += ApplySettings;
        LobbyStateMsg.Received += OnLobbyState;
        WelcomeMsg.Received += ApplyWelcome;
    }

    public override void _ExitTree()
    {
        LobbySettingsMsg.Received -= ApplySettings;
        LobbyStateMsg.Received -= OnLobbyState;
        WelcomeMsg.Received -= ApplyWelcome;
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
        if (message.ModeIds.Length != message.ModeNames.Length ||
            message.ModeIds.Length > NetConfig.MAX_LOBBY_MODES)
        {
            SetError("Server sent an invalid mode catalog.");
            return;
        }

        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
        }
        catch (IOException)
        {
            SetError("Server sent invalid match settings.");
            return;
        }

        bool settingsChanged = !HasServerState || SettingsError != "" ||
                               MapId != message.MapId || MapHash != message.MapHash ||
                               ModeId != message.ModeId ||
                               CatalogChanged(_mapOptions, message.MapIds, message.MapNames) ||
                               CatalogChanged(_modeOptions, message.ModeIds, message.ModeNames);
        HasServerState = true;
        SettingsError = "";
        MapId = message.MapId;
        MapHash = message.MapHash;
        ModeId = message.ModeId;
        _mapOptions.Clear();
        for (int i = 0; i < message.MapIds.Length; i++)
        {
            _mapOptions.Add(new ContentOption(message.MapIds[i], message.MapNames[i]));
        }
        _modeOptions.Clear();
        for (int i = 0; i < message.ModeIds.Length; i++)
        {
            _modeOptions.Add(new ContentOption(message.ModeIds[i], message.ModeNames[i]));
        }
        ApplyConfig(config, settingsChanged);
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
        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
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
        ApplyConfig(config, settingsChanged);
    }

    private void ApplyConfig(MatchConfig config, bool raiseSettings)
    {
        byte[] bytes = config.ToBytes();
        bool configChanged = !bytes.AsSpan().SequenceEqual(_configBytes);
        bool teamsToggled = Config.Rules.Teams != config.Rules.Teams;
        Config = config;
        _configBytes = bytes;
        if (configChanged)
            ConfigChanged?.Invoke();
        if (teamsToggled)
            TeamsChanged?.Invoke();
        if (raiseSettings || configChanged)
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

    private static bool CatalogChanged(List<ContentOption> options, string[] ids, string[] names)
    {
        if (options.Count != ids.Length)
            return true;
        for (int i = 0; i < ids.Length; i++)
        {
            if (options[i].Id != ids[i] || options[i].Name != names[i])
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
