using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Hosting;
using Mortz.Client.MapEditor;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Servers;
using Mortz.Client.Settings;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Platform;
using Mortz.Protocol.Hosting;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Mortz.Protocol.Net.Lobby;
using Mortz.Protocol.Net.Sim;
using Mortz.Protocol.Terrain;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
using CryptoRandom = System.Security.Cryptography.RandomNumberGenerator;

#if TOOLS
using Mortz.Shared.E2E;
#endif

namespace Mortz.Client.Session;

/// <summary>Owns connection, session, lobby, and match-scene transitions for
/// one client.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientSessionController : Node, ISessionExit,
    IHandle<LobbyLoadMsg>,
    IHandle<MatchLoadMsg>,
    IHandle<TerrainChunkMsg>,
    IProvide<ISessionExit>, IProvide<ServerBrowserController>
{
    private static readonly ILogger _log = MortzLog.For("client");

    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;
    [Export] private PackedScene _lobbyScene = null!;
    [Export] private PackedScene _sessionScene = null!;
    [Export] private PackedScene _menuScene = null!;
    [Export] private PackedScene _mapEditorScene = null!;

    private readonly ClientConnectionAttempt _connection = new(CONNECT_RETRIES);
    private readonly ClientSession _session = new();
    [Dependency] private ClientSettings Settings => this.DependOn<ClientSettings>();
    [Dependency] private IClientTicketProvider Tickets => this.DependOn<IClientTicketProvider>();
    [Dependency] private IInternetDiscovery Discovery => this.DependOn<IInternetDiscovery>();
    private ulong _knownRecipient;
    private ClientAdmission? _admission;
    private ServerBrowserController? _browser;
    private PendingMatchEntry? _pendingMatch;
    private ConnectedSession? _connectedSession;
    private ClientMatchState? _matchState;
    private ClientMatchRuntime? _matchRuntime;
    private GameView? _gameView;
    private Lobby? _lobby;
    private MainMenu? _menu;
    private MapEditorScreen? _mapEditor;
    private string? _pendingLocalAdminPassword;
    private OwnedServerProcess? _localServer;
    private Task<HostControlMessage>? _localStartup;
    private Task? _localStop;
    private string _hostPlayerName = "";
    private int _hostSkin;
    private bool _subscribed;

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    ISessionExit IProvide<ISessionExit>.Value() => this;
    ServerBrowserController IProvide<ServerBrowserController>.Value() => _browser!;

    public override void _Notification(int what)
    {
        this.Notify(what);
    }

    public void OnResolved()
    {
        _admission = new ClientAdmission(Tickets, Network, Time.GetTicksMsec);
        _admission.Accepted += OnAdmitted;
        _admission.Rejected += OnAdmissionRejected;
        PlatformRuntimeOwner admissionLifetime = new();
        admissionLifetime.Initialize(() =>
        {
            _admission.Advance(Time.GetTicksMsec());
            _browser?.Advance();
            AdvanceLocalServer();
        }, _admission.Dispose);
        AddChild(admissionLifetime);
        ServerProbe probe = new() { Name = "ServerProbe" };
        AddChild(probe);
        _browser = new ServerBrowserController(probe, () => Settings.Favorites, Settings.SetFavorites, Discovery, Time.GetTicksMsec);
        _browser.JoinRequested += OnBrowserJoinRequested;
        this.Provide();
        Subscribe();
        CreateMenu(autoStartIntro: false);
        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect == null)
            return;
        string playerName = CmdArgs.GetValue("--name") ?? Settings.PlayerName;
        int skin = CmdArgs.GetInt("--skin", Settings.Skin);
        if (!ClientSettings.IsValidSkin(skin))
        {
            _log.Error("invalid --skin {Skin}, using 0", skin);
            skin = 0;
        }
        StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT),
            playerName, skin);
    }

    public void OnExitTree()
    {
        _admission?.Dispose();
        if (_browser != null)
        {
            _browser.JoinRequested -= OnBrowserJoinRequested;
            _browser.Dispose();
        }
        Unsubscribe();
        _connection.Cancel();
        StopLocalServer();
        try
        {
            _localStop?.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _log.Error(exception, "failed to stop local server");
        }
    }

    public void OnHostRequested(int port, string playerName, string adminPassword,
        string serverName, int skin = 0, bool allowJoinInProgress = true)
    {
        if (_localStop is { IsCompleted: false })
        {
            _menu?.SetStatus("The previous local server is still stopping. Try again shortly.");
            return;
        }
        if (_localServer != null || !_session.TryBeginConnecting())
        {
            return;
        }
        _connection.Cancel();
        Network.ResetPeer();
        _browser?.Close();
        string localAdminPassword = adminPassword.Length > 0
            ? adminPassword
            : Convert.ToHexString(CryptoRandom.GetBytes(32));
        try
        {
            _localServer = new OwnedServerProcess();
            _localStartup = _localServer.StartAsync(ServerLauncher.CreateStartInfo(port,
                localAdminPassword, serverName, allowJoinInProgress));
            _pendingLocalAdminPassword = localAdminPassword;
            _hostPlayerName = playerName;
            _hostSkin = skin;
            _menu?.SetStatus("Starting local server...");
        }
        catch (Exception exception)
        {
            ReturnToMenu($"Failed to start local server: {exception.Message}", stopLocalServer: true);
        }
    }

    public void OnJoinRequested(string address, int port, string playerName, int skin = 0)
    {
        if (_session.Stage is not (ClientSessionStage.MENU or ClientSessionStage.CONNECTING))
        {
            return;
        }
        StopLocalServer();
        _pendingLocalAdminPassword = null;
        StartConnecting(address, port, playerName, skin);
    }

    private void OnBrowserJoinRequested(ServerJoinRequest request)
    {
        if (_session.Stage is not (ClientSessionStage.MENU or ClientSessionStage.CONNECTING))
        {
            return;
        }
        StopLocalServer();
        _pendingLocalAdminPassword = null;
        StartConnecting(request.Address, request.Port, Settings.PlayerName, Settings.Skin, request.KnownSteamAccountId);
    }

    private void AdvanceLocalServer()
    {
        if (_localStop is { IsCompleted: true } stop)
        {
            _localStop = null;
            if (stop.IsFaulted)
            {
                _log.Error(stop.Exception, "failed to stop local server");
                _menu?.SetStatus("Failed to stop local server. See the log for details.");
            }
        }
        if (_localStartup is { IsCompleted: true } startup)
        {
            _localStartup = null;
            try
            {
                HostControlMessage ready = startup.GetAwaiter().GetResult();
                _log.Information("local server ready at {Address}:{Port}, query port {QueryPort}",
                    ready.Address, ready.GamePort, ready.QueryPort);
                StartConnecting(ready.Address, ready.GamePort, _hostPlayerName, _hostSkin);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "local server startup failed");
                ReturnToMenu(exception.Message, stopLocalServer: true);
            }
        }
        else if (_localStartup == null && _localServer?.HasExited == true)
        {
            ReturnToMenu("Local server stopped unexpectedly.", stopLocalServer: true);
        }
    }

    private void StopLocalServer()
    {
        if (_localServer == null)
        {
            return;
        }
        OwnedServerProcess server = _localServer;
        Task<HostControlMessage>? startup = _localStartup;
        _localServer = null;
        _localStartup = null;
        _localStop = FinishLocalServerAsync(server, startup);
    }

    private static async Task FinishLocalServerAsync(OwnedServerProcess server, Task<HostControlMessage>? startup)
    {
        await server.StopAsync().ConfigureAwait(false);
        if (startup != null)
        {
            try { await startup.ConfigureAwait(false); }
            catch (Exception) { /* A cancelled launch has no result to present. */ }
        }
    }

    private void CancelMenuConnection()
    {
        if (_session.Stage == ClientSessionStage.CONNECTING)
        {
            ReturnToMenu("", stopLocalServer: true);
        }
    }

    public void OnReadyToggled(bool ready) => new SetReadyMsg(ready).SendToServer(Network);

    public void LeaveSession(string reason) => ReturnToMenu(reason, stopLocalServer: true);

    private void Subscribe()
    {
        Network.OfferReceived += OnOffer;
        Network.AdmissionAcceptedReceived += OnAccepted;
        Network.AdmissionRejectedReceived += OnRejected;
        Network.TransportReset += OnTransportReset;
        Network.Connected += OnConnected;
        Network.ConnectionFailed += OnConnectionFailed;
        Network.Disconnected += OnDisconnected;
        Network.Router.Add(this);
        Network.SnapshotReceived += ReceiveSnapshot;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        Network.OfferReceived -= OnOffer;
        Network.AdmissionAcceptedReceived -= OnAccepted;
        Network.AdmissionRejectedReceived -= OnRejected;
        Network.TransportReset -= OnTransportReset;
        Network.Connected -= OnConnected;
        Network.ConnectionFailed -= OnConnectionFailed;
        Network.Disconnected -= OnDisconnected;
        Network.Router.Remove(this);
        Network.SnapshotReceived -= ReceiveSnapshot;
        _subscribed = false;
    }

    private void StartConnecting(string address, int port, string playerName, int skin, ulong knownRecipient = 0)
    {
        if (!_session.TryBeginConnecting())
            return;
        _knownRecipient = knownRecipient;
        _browser?.Close();
        _connection.Start(address, port, playerName, skin);
        _pendingMatch = null;
        _menu?.SetStatus($"Connecting to {address}:{port}...");
        _log.Information("connecting to {Address}:{Port}", address, port);
        TryConnect();
    }

    private void TryConnect()
    {
        Network.ResetPeer();
        Error error = Network.StartClient(_connection.Address, _connection.Port);
        if (error != Error.Ok)
            OnConnectionFailed();
    }

    private async void OnConnectionFailed()
    {
        ConnectionFailure failure = _connection.Failed();
        if (failure.Action == ConnectionFailureAction.IGNORE)
            return;
        if (failure.Action == ConnectionFailureAction.RETRY)
        {
            _menu?.SetStatus($"Retrying... ({failure.RetryNumber}/{failure.MaxRetries})");
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            if (_connection.BeginScheduledRetry(failure.Generation))
                TryConnect();
            return;
        }

        _log.Information("connection failed");
        ReturnToMenu("Connection failed.", stopLocalServer: true);
    }

    private void OnConnected()
    {
        _connection.Connected();
        _log.Information("connected, peer id {PeerId}", Network.LocalPeerId);
        _menu?.SetStatus("Waiting for admission...");
        _admission!.Begin(_connection.PlayerName, _connection.Skin, Time.GetTicksMsec(), _knownRecipient);
    }

    private void OnOffer(AdmissionOffer offer)
    {
        _menu?.SetStatus(offer.Mode == AdmissionMode.STEAM ? "Verifying Steam account..." : "Joining as guest...");
        _admission?.Offer(offer, Time.GetTicksMsec());
    }

    private void OnAccepted(AdmissionAccepted accepted) => _admission?.Accept(accepted);
    private void OnRejected(AdmissionRejected rejected) => _admission?.Reject(rejected);
    private void OnTransportReset() => _admission?.Reset();

    private void OnAdmitted(AdmissionMode mode)
    {
        Network.MarkClientAdmitted();
        CreateConnectedSession();
        _menu?.SetStatus("Entering game...");
        _log.Information("admitted as {Mode}, peer id {PeerId}", mode, Network.LocalPeerId);
    }

    private void OnAdmissionRejected(AdmissionRejection reason)
    {
        _log.Information("admission rejected: {Reason}", reason);
        ReturnToMenu(AdmissionReasons.Describe(reason), stopLocalServer: true);
    }

    public void Handle(in LobbyLoadMsg message)
    {
        if (Network.Router.MatchGeneration is int current && message.Generation < current)
            return;
        bool returningFromMatch = _session.Stage is
            ClientSessionStage.LOADING_MATCH or ClientSessionStage.PLAYING;
        if (!_session.TryEnterLobby())
            return;
        if (returningFromMatch)
        {
            DisposeGameView();
            _pendingMatch = null;
        }
        Network.Router.MatchGeneration = message.Generation;
        DisposeMenu();
        CreateLobby(message.Generation);
    }

    public void Handle(in MatchLoadMsg message)
    {
        if (Network.Router.MatchGeneration is int current && message.Generation < current)
            return;
        if (!_session.TryBeginMatchLoad())
            return;
        if (!PendingMatchEntry.TryCreate(message, out PendingMatchEntry? bootstrap,
                out string error))
        {
            RejectMatchLoad(error);
            return;
        }

        _log.Information("map '{Map}' verified", bootstrap!.Map.DisplayName);
        Network.Router.MatchGeneration = message.Generation;
        _pendingMatch = bootstrap;
    }

    public void Handle(in TerrainChunkMsg message)
    {
        if (_pendingMatch is not PendingMatchEntry pending)
            return;
        TerrainChunkResult result = pending.Terrain.Accept(message);
        if (result.State is TerrainChunkState.IGNORED or TerrainChunkState.WAITING)
            return;
        if (result.State == TerrainChunkState.REJECTED)
        {
            RejectMatchLoad(result.Error);
            return;
        }

        EnterMatch(pending, result.Data!);
    }

    private void EnterMatch(PendingMatchEntry entry, byte[] terrainData)
    {
        if (_connectedSession == null || !_session.TryEnterMatch())
            return;
#if TOOLS
        if (E2ELaunch.ScreenLoadDelayMs > 0)
        {
            DelayMatchEntry(entry, terrainData);
            return;
        }
#endif
        MountMatch(entry, terrainData);
    }

#if TOOLS
    private async void DelayMatchEntry(PendingMatchEntry entry, byte[] terrainData)
    {
        await ToSignal(GetTree().CreateTimer(
            E2ELaunch.ScreenLoadDelayMs / 1000.0),
            SceneTreeTimer.SignalName.Timeout);
        if (_pendingMatch == entry && _connectedSession != null)
            MountMatch(entry, terrainData);
    }
#endif

    private void MountMatch(PendingMatchEntry entry, byte[] terrainData)
    {
        if (_connectedSession is not ConnectedSession connectedSession)
            return;
        ClientMatchState matchState = new(entry.Generation, entry.Participation);
        TerrainMask mask = entry.Map.BuildMask();
        try
        {
            TerrainSync.Apply(mask, entry.Terrain.Encoding, terrainData);
        }
        catch (IOException exception)
        {
            RejectMatchLoad($"Invalid terrain sync: {exception.Message}");
            return;
        }
        DisposeGameView();
        ClientMatchRuntime runtime = connectedSession.Connection.OpenMatch(matchState, mask,
            entry.Terrain.Config, entry.Map.Zones, Network.LocalPeerId,
            Network.SendInputs, Time.GetTicksMsec);
        if (!runtime.InitializeSnapshot(entry.InitialSnapshot, entry.InitialSnapshotAck))
        {
            RejectMatchLoad("Invalid initial match snapshot.");
            return;
        }
        GameView gameView = _gameViewScene.Instantiate<GameView>();
        try
        {
            gameView.Initialize(runtime, entry.Map);
        }
        catch (IOException exception)
        {
            runtime.Dispose();
            gameView.Free();
            RejectMatchLoad($"Invalid terrain sync: {exception.Message}");
            return;
        }

        // Joining straight into a running match never passes through the lobby,
        // so this is the only place that unmounts the menu on that path.
        DisposeMenu();
        DisposeLobby();

        _matchState = matchState;
        _matchRuntime = runtime;
        _gameView = gameView;
        connectedSession.AddChild(gameView);
        _pendingMatch = null;
        new PhaseReadyMsg(entry.Generation).SendToServer(Network);
    }

    private void ReceiveSnapshot(byte[] data, int ack) => _matchRuntime?.AcceptSnapshot(data, ack);

    public override void _PhysicsProcess(double delta)
    {
        if (_matchRuntime is ClientMatchRuntime runtime)
            runtime.Tick(runtime.SampleInput?.Invoke() ?? default);
    }

    public override void _Process(double delta) => _matchRuntime?.Advance((float)delta);

    private void RejectMatchLoad(string reason)
    {
        _log.Error("{Reason} Disconnecting.", reason);
        ReturnToMenu(reason, stopLocalServer: true);
    }

    private void OnDisconnected()
    {
        _log.Information("disconnected from server");
        ReturnToMenu("Disconnected.", stopLocalServer: true);
    }

    // Drops the peer itself: reaching the menu with a live connection would
    // leave the player in the server's roster with no way back to the session.
    private void ReturnToMenu(string status, bool stopLocalServer)
    {
        _connection.Cancel();
        Network.ResetPeer();
        DisposeGameView();
        DisposeLobby();
        DisposeConnectedSession();
        _pendingMatch = null;
        _pendingLocalAdminPassword = null;
        _session.ReturnToMenu();
        CreateMenu(autoStartIntro: true);
        _menu!.ShowHome();
        _menu.SetStatus(status);
        if (stopLocalServer)
        {
            StopLocalServer();
        }
    }

    private void CreateMenu(bool autoStartIntro)
    {
        if (_menu != null)
            return;
        _menu = _menuScene.Instantiate<MainMenu>();
        _menu.HostRequested += OnHostRequested;
        _menu.NavigationRequested += CancelMenuConnection;
        _menu.MapEditorRequested += OpenMapEditor;
        AddChild(_menu);
        if (autoStartIntro)
            _menu.AutoStartIntro();
    }

    private void DisposeMenu()
    {
        Detach(_menu);
        _menu = null;
    }

    private void OpenMapEditor()
    {
        if (_mapEditor != null)
            return;
        DisposeMenu();
        _mapEditor = _mapEditorScene.Instantiate<MapEditorScreen>();
        _mapEditor.Closed += CloseMapEditor;
        AddChild(_mapEditor);
    }

    private void CloseMapEditor()
    {
        if (_mapEditor != null)
            _mapEditor.Closed -= CloseMapEditor;
        Detach(_mapEditor);
        _mapEditor = null;
        CreateMenu(autoStartIntro: true);
        _menu!.ShowHome();
    }

    private void CreateConnectedSession()
    {
        if (_connectedSession != null)
            return;
        _connectedSession = _sessionScene.Instantiate<ConnectedSession>();
        AddChild(_connectedSession);
    }

    private void DisposeConnectedSession()
    {
        Detach(_connectedSession);
        _connectedSession = null;
    }

    private void CreateLobby(int generation)
    {
        if (_lobby != null || _connectedSession == null)
            return;
#if TOOLS
        if (E2ELaunch.ScreenLoadDelayMs > 0)
        {
            DelayLobbyEntry(generation);
            return;
        }
#endif
        MountLobby(generation);
    }

#if TOOLS
    private async void DelayLobbyEntry(int generation)
    {
        ConnectedSession? connection = _connectedSession;
        await ToSignal(GetTree().CreateTimer(
            E2ELaunch.ScreenLoadDelayMs / 1000.0),
            SceneTreeTimer.SignalName.Timeout);
        if (_lobby == null && connection != null && ReferenceEquals(connection, _connectedSession) &&
            _session.Stage == ClientSessionStage.LOBBY)
            MountLobby(generation);
    }
#endif

    private void MountLobby(int generation)
    {
        if (_connectedSession is not ConnectedSession connectedSession)
            return;
        _lobby = _lobbyScene.Instantiate<Lobby>();
        _lobby.Initialize(generation);
        _lobby.ReadyToggled += OnReadyToggled;
        connectedSession.AddChild(_lobby);
        if (_pendingLocalAdminPassword is string password)
        {
            _pendingLocalAdminPassword = null;
            connectedSession.Admin.BeginAuthentication(password);
        }
    }

    private void DisposeLobby()
    {
        Detach(_lobby);
        _lobby = null;
    }

    private void DisposeGameView()
    {
        _connectedSession?.Connection.CloseMatch();
        _matchRuntime = null;
        _matchState?.Close();
        _matchState = null;
        Detach(_gameView);
        _gameView = null;
    }

    // QueueFree alone defers the exit to end of frame, so a dying screen's
    // handlers would stay routed alongside the next screen's. Detach first so
    // router membership changes with the transition; QueueFree still frees
    // the node safely at frame end.
    private static void Detach(Node? node)
    {
        if (node == null)
            return;
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }
}
