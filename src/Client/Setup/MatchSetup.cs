using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Sim;

namespace Mortz.Client.Setup;

/// <summary>Server-owned lobby state for the UI to read. Events fire when a
/// value changes, not once per message.</summary>
[Meta(typeof(IAutoNode))]
public partial class MatchSetup : Node,
    IHandle<LobbySettingsMsg>,
    IHandle<LobbyStateMsg>,
    IHandle<WelcomeMsg>
{
    private readonly List<SwapOffer> _swapOffers = [];
    private byte[] _configBytes;

    public event Action? ConfigChanged;

    /// <summary>The Teams rule toggled. Assignment moves are identity and
    /// arrive through ClientPlayers.Changed.</summary>
    public event Action? TeamsChanged;

    /// <summary>The selected map, catalog, rules, or settings error changed.</summary>
    public event Action? SettingsChanged;

    /// <summary>A pending swap offer appeared, resolved, or expired.</summary>
    public event Action? SwapOffersChanged;

    /// <summary>Null until the first valid server settings arrive; a welcome
    /// carries no catalogs, so it never produces one.</summary>
    public LobbySelection? Selection { get; private set; }

    public MatchConfig Config { get; private set; } = new();

    /// <summary>Null while the last received server settings were valid.</summary>
    public string? SettingsError { get; private set; }

    public IReadOnlyList<SwapOffer> SwapOffers => _swapOffers;

    public MatchSetup() => _configBytes = Config.ToBytes();

    public MatchConfig CopyConfig() => MatchConfig.FromBytes(_configBytes);

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

    public void Handle(in LobbySettingsMsg message)
    {
        if (LobbySettingsProtocol.TryDecode(message, out LobbySettings? settings,
                out LobbySettingsRejectReason reason))
            ApplySettings(settings);
        else
            OnSettingsRejected(reason);
    }

    public void Handle(in LobbyStateMsg message) => ApplyOffers(message.Offers);

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
    public void Handle(in WelcomeMsg message)
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

    private void SetError(string error)
    {
        if (SettingsError == error)
            return;
        SettingsError = error;
        SettingsChanged?.Invoke();
    }
}
