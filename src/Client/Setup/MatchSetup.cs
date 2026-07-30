using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Setup;

/// <summary>Server-owned lobby state for the UI to read. Events fire when a
/// value changes, not once per message.</summary>
public partial class MatchSetup : Node
{
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

    /// <summary>Null until the first valid server settings arrive; a welcome
    /// carries no catalogs, so it never produces one.</summary>
    public LobbySelection? Selection { get; private set; }

    public MatchConfig Config { get; private set; } = new();

    /// <summary>Null while the last received server settings were valid.</summary>
    public string? SettingsError { get; private set; }

    public IReadOnlyList<LobbyMember> Members => _members;
    public IReadOnlyList<SwapOffer> SwapOffers => _swapOffers;

    public MatchSetup() => _configBytes = Config.ToBytes();

    public MatchConfig CopyConfig() => MatchConfig.FromBytes(_configBytes);

    public override void _Ready()
    {
        LobbySettingsProtocol.Received += ApplySettings;
        LobbySettingsProtocol.Rejected += OnSettingsRejected;
        RosterProtocol.LobbyRosterReceived += OnLobbyRoster;
        WelcomeMsg.Received += ApplyWelcome;
    }

    public override void _ExitTree()
    {
        LobbySettingsProtocol.Received -= ApplySettings;
        LobbySettingsProtocol.Rejected -= OnSettingsRejected;
        RosterProtocol.LobbyRosterReceived -= OnLobbyRoster;
        WelcomeMsg.Received -= ApplyWelcome;
    }

    private void OnLobbyRoster(LobbyRoster roster)
    {
        ApplyMembers(roster.Members);
        ApplyOffers(roster.Offers);
        new LobbySettingsRequestMsg().SendToServer();
    }

    private void ApplySettings(LobbySettings settings)
    {
        // Deconstructed so a new LobbySettings member breaks this line instead
        // of slipping past change detection.
        (LobbySelection selection, MatchConfig config) = settings;
        bool selectionChanged = SettingsError != null || Selection != selection;
        SettingsError = null;
        Selection = selection;
        ApplyConfig(config, selectionChanged);
    }

    private void OnSettingsRejected(LobbySettingsRejectReason reason) => SetError(reason switch
    {
        LobbySettingsRejectReason.MAP_CATALOG => "Server sent an invalid map catalog.",
        LobbySettingsRejectReason.MODE_CATALOG => "Server sent an invalid mode catalog.",
        LobbySettingsRejectReason.CONFIG => "Server sent invalid match settings.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    });

    private void ApplyOffers(IReadOnlyList<SwapOffer> offers)
    {
        if (offers.SequenceEqual(_swapOffers))
            return;
        _swapOffers.Clear();
        _swapOffers.AddRange(offers);
        SwapOffersChanged?.Invoke();
    }

    /// <summary>Mid-match rules for a player who never saw a lobby broadcast.
    /// It carries no catalogs, so it sets no Selection; the match screen gets
    /// its map from ClientMatchBootstrap.</summary>
    private void ApplyWelcome(WelcomeMsg message)
    {
        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return; // the session controller rejects the welcome itself
        }

        ApplyConfig(config, raiseSettings: false);
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

    private static LobbyMember WithoutTeam(LobbyMember member) => member.OnTeam(null);

    /// <summary>Only real assignments count, so joins and leaves in a
    /// teamless lobby never read as team movement.</summary>
    private static IEnumerable<TeamAssignment> Assignments(IEnumerable<LobbyMember> members)
    {
        foreach (LobbyMember member in members)
        {
            if (member.Team is Team team)
                yield return new TeamAssignment(member.PeerId, team);
        }
    }

    private void SetError(string error)
    {
        if (SettingsError == error)
            return;
        SettingsError = error;
        SettingsChanged?.Invoke();
    }
}
