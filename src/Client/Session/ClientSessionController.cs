using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.MapEditor;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Settings;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Sim;
using Mortz.Net;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
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
    IProvide<ISessionExit>, IProvide<ClientSettings>
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
    private ClientSettings _settings = new();
    private PendingMatchEntry? _pendingMatch;
    private ConnectedSession? _connectedSession;
    private GameView? _gameView;
    private Lobby? _lobby;
    private MainMenu? _menu;
    private MapEditorScreen? _mapEditor;
    private bool _spawnedLocalServer;
    private bool _subscribed;

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    ISessionExit IProvide<ISessionExit>.Value() => this;
    ClientSettings IProvide<ClientSettings>.Value() => _settings;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            ServerLauncher.Kill();
        this.Notify(what);
    }

    public void OnResolved()
    {
        _settings = ClientSettings.Load();
        this.Provide();
        Subscribe();
        CreateMenu(autoStartIntro: false);
        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect == null)
            return;
        string playerName = CmdArgs.GetValue("--name") ?? _settings.PlayerName;
        int skin = CmdArgs.GetInt("--skin", _settings.Skin);
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
        Unsubscribe();
        _connection.Cancel();
        ServerLauncher.Kill();
    }

    public void OnHostRequested(int port, string playerName, string adminPassword,
        string serverName, int skin = 0, bool allowJoinInProgress = true)
    {
        if (!ServerLauncher.Spawn(port, adminPassword, serverName, allowJoinInProgress))
        {
            _menu?.SetStatus("Failed to start local server.");
            return;
        }
        _spawnedLocalServer = true;
        StartConnecting("127.0.0.1", port, playerName, skin);
    }

    public void OnJoinRequested(string address, int port, string playerName, int skin = 0) =>
        StartConnecting(address, port, playerName, skin);

    public void OnReadyToggled(bool ready) => new SetReadyMsg(ready).SendToServer();

    public void LeaveSession(string reason) => ReturnToMenu(reason, stopLocalServer: true);

    private void Subscribe()
    {
        Network.Connected += OnConnected;
        Network.ConnectionFailed += OnConnectionFailed;
        Network.Disconnected += OnDisconnected;
        Network.Router.Add(this);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        Network.Connected -= OnConnected;
        Network.ConnectionFailed -= OnConnectionFailed;
        Network.Disconnected -= OnDisconnected;
        Network.Router.Remove(this);
        _subscribed = false;
    }

    private void StartConnecting(string address, int port, string playerName, int skin)
    {
        if (!_session.TryBeginConnecting())
            return;
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
        CreateConnectedSession();
        Network.SendHello(_connection.PlayerName, _connection.Skin);
        _menu?.SetStatus("Entering lobby...");
    }

    public void Handle(in LobbyLoadMsg message)
    {
        bool returningFromMatch = _session.Stage is
            ClientSessionStage.LOADING_MATCH or ClientSessionStage.PLAYING;
        if (!_session.TryEnterLobby())
            return;
        if (returningFromMatch)
        {
            DisposeGameView();
            _pendingMatch = null;
        }
        DisposeMenu();
        CreateLobby(message.Generation);
    }

    public void Handle(in MatchLoadMsg message)
    {
        if (!_session.TryBeginMatchLoad())
            return;
        if (!PendingMatchEntry.TryCreate(message, out PendingMatchEntry? bootstrap,
                out string error))
        {
            RejectMatchLoad(error);
            return;
        }

        _log.Information("map '{Map}' verified", bootstrap!.Map.DisplayName);
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
        GameView gameView = _gameViewScene.Instantiate<GameView>();
        try
        {
            gameView.Initialize(entry.Generation, entry.Map, entry.Terrain.Config,
                entry.Terrain.Encoding, terrainData, entry.Participation,
                entry.InitialSnapshot, entry.InitialSnapshotAck);
        }
        catch (IOException exception)
        {
            gameView.Free();
            RejectMatchLoad($"Invalid terrain sync: {exception.Message}");
            return;
        }

        // Joining straight into a running match never passes through the lobby,
        // so this is the only place that unmounts the menu on that path.
        DisposeMenu();
        DisposeLobby();
        DisposeGameView();
        connectedSession.Players.OpenMatch(entry.Terrain.Config);
        _gameView = gameView;
        connectedSession.AddChild(gameView);
        _pendingMatch = null;
    }

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
        _session.ReturnToMenu();
        CreateMenu(autoStartIntro: true);
        _menu!.ShowHome();
        _menu.SetStatus(status);
        if (stopLocalServer && _spawnedLocalServer)
        {
            ServerLauncher.Kill();
            _spawnedLocalServer = false;
        }
    }

    private void CreateMenu(bool autoStartIntro)
    {
        if (_menu != null)
            return;
        _menu = _menuScene.Instantiate<MainMenu>();
        _menu.HostRequested += OnHostRequested;
        _menu.JoinRequested += OnJoinRequested;
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
        await ToSignal(GetTree().CreateTimer(
            E2ELaunch.ScreenLoadDelayMs / 1000.0),
            SceneTreeTimer.SignalName.Timeout);
        if (_lobby == null && _connectedSession != null &&
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
    }

    private void DisposeLobby()
    {
        Detach(_lobby);
        _lobby = null;
    }

    private void DisposeGameView()
    {
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
