using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Session;

/// <summary>Owns connection, session, lobby, and match-scene transitions for
/// one client.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientSessionController : Node, ISessionExit, IProvide<ISessionExit>
{
    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;
    [Export] private PackedScene _lobbyScene = null!;
    [Export] private PackedScene _sessionScene = null!;
    [Export] private PackedScene _menuScene = null!;

    private readonly ClientConnectionAttempt _connection = new(CONNECT_RETRIES);
    private readonly ClientSession _session = new();
    private ClientMatchBootstrap? _pendingMatch;
    private ConnectedSession? _connectedSession;
    private GameView? _gameView;
    private Lobby? _lobby;
    private MainMenu? _menu;
    private bool _spawnedLocalServer;
    private bool _autoReady;
    private bool _subscribed;

    [Dependency]
    private NetworkManager Network => this.DependOn<NetworkManager>();

    ISessionExit IProvide<ISessionExit>.Value() => this;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            ServerLauncher.Kill();
        this.Notify(what);
    }

    public void OnResolved()
    {
        this.Provide();
        Subscribe();
        CreateMenu(autoStartIntro: false);
        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect == null)
            return;
        _autoReady = true;
        string playerName = CmdArgs.GetValue("--name") ?? "";
        StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT), playerName);
    }

    public void OnExitTree()
    {
        Unsubscribe();
        _connection.Cancel();
        ServerLauncher.Kill();
    }

    public void OnHostRequested(int port, string playerName, string adminPassword)
    {
        if (!ServerLauncher.Spawn(port, adminPassword))
        {
            _menu?.SetStatus("Failed to start local server.");
            return;
        }
        _spawnedLocalServer = true;
        StartConnecting("127.0.0.1", port, playerName);
    }

    public void OnJoinRequested(string address, int port, string playerName) =>
        StartConnecting(address, port, playerName);

    public void OnReadyToggled(bool ready) => new SetReadyMsg(ready).SendToServer();

    public void LeaveSession(string reason) => ReturnToMenu(reason, stopLocalServer: true);

    private void Subscribe()
    {
        Network.Connected += OnConnected;
        Network.ConnectionFailed += OnConnectionFailed;
        Network.Disconnected += OnDisconnected;
        LobbyStateMsg.Received += OnLobbyState;
        WelcomeMsg.Received += OnWelcome;
        TerrainChunkMsg.Received += OnTerrainChunk;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        Network.Connected -= OnConnected;
        Network.ConnectionFailed -= OnConnectionFailed;
        Network.Disconnected -= OnDisconnected;
        LobbyStateMsg.Received -= OnLobbyState;
        WelcomeMsg.Received -= OnWelcome;
        TerrainChunkMsg.Received -= OnTerrainChunk;
        _subscribed = false;
    }

    private void StartConnecting(string address, int port, string playerName)
    {
        _connection.Start(address, port, playerName);
        _session.BeginConnecting();
        _pendingMatch = null;
        _menu?.SetStatus($"Connecting to {address}:{port}...");
        GD.Print($"[client] connecting to {address}:{port}");
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

        GD.Print("[client] connection failed");
        ReturnToMenu("Connection failed.", stopLocalServer: true);
    }

    private void OnConnected()
    {
        _connection.Connected();
        GD.Print($"[client] connected, peer id {Network.LocalPeerId}");
        CreateConnectedSession();
        Network.SendHello(_connection.PlayerName);
        _menu?.SetStatus("Entering lobby...");
        if (_autoReady)
            new SetReadyMsg(true).SendToServer();
    }

    private void OnLobbyState(LobbyStateMsg message)
    {
        bool returningFromMatch = _session.Stage is
            ClientSessionStage.LOADING_MATCH or ClientSessionStage.PLAYING;
        if (!_session.TryEnterLobby())
            return;
        if (returningFromMatch)
        {
            DisposeGameView();
            _pendingMatch = null;
            if (_autoReady)
                new SetReadyMsg(true).SendToServer();
        }
        DisposeMenu();
        CreateLobby();
    }

    private void OnWelcome(WelcomeMsg message)
    {
        if (!_session.TryBeginMatchLoad())
            return;
        if (!ClientMatchBootstrap.TryCreate(message, out ClientMatchBootstrap? bootstrap,
                out string error))
        {
            RejectWelcome(error);
            return;
        }

        GD.Print($"[client] map '{bootstrap!.Map.DisplayName}' verified");
        _pendingMatch = bootstrap;
    }

    private void OnTerrainChunk(TerrainChunkMsg message)
    {
        if (_pendingMatch is not { } pending)
            return;
        TerrainChunkResult result = pending.Terrain.Accept(message);
        if (result.State is TerrainChunkState.IGNORED or TerrainChunkState.WAITING)
            return;
        if (result.State == TerrainChunkState.REJECTED)
        {
            RejectWelcome(result.Error);
            return;
        }

        EnterMatch(pending, result.Data!);
    }

    private void EnterMatch(ClientMatchBootstrap bootstrap, byte[] terrainData)
    {
        if (_connectedSession == null || !_session.TryEnterMatch())
            return;
        GameView gameView = _gameViewScene.Instantiate<GameView>();
        try
        {
            gameView.Initialize(bootstrap.Map, bootstrap.Terrain.Config,
                bootstrap.Terrain.Encoding, terrainData);
        }
        catch (IOException exception)
        {
            gameView.Free();
            RejectWelcome($"Invalid terrain sync: {exception.Message}");
            return;
        }

        DisposeLobby();
        _gameView = gameView;
        _connectedSession.AddChild(gameView);
        _pendingMatch = null;
    }

    private void RejectWelcome(string reason)
    {
        GD.PrintErr($"[client] {reason} Disconnecting.");
        ReturnToMenu(reason, stopLocalServer: true);
    }

    private void OnDisconnected()
    {
        GD.Print("[client] disconnected from server");
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
        AddChild(_menu);
        if (autoStartIntro)
            _menu.AutoStartIntro();
    }

    private void DisposeMenu()
    {
        _menu?.QueueFree();
        _menu = null;
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
        _connectedSession?.QueueFree();
        _connectedSession = null;
    }

    private void CreateLobby()
    {
        if (_lobby != null || _connectedSession == null)
            return;
        _lobby = _lobbyScene.Instantiate<Lobby>();
        _lobby.ReadyToggled += OnReadyToggled;
        _connectedSession.AddChild(_lobby);
    }

    private void DisposeLobby()
    {
        _lobby?.QueueFree();
        _lobby = null;
    }

    private void DisposeGameView()
    {
        _gameView?.QueueFree();
        _gameView = null;
    }
}
