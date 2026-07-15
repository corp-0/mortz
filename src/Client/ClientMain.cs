using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>Godot adapter for client session flow. Connection retry policy,
/// session state and match bootstrap assembly have focused owners; this node
/// translates their outcomes into UI and scene changes.</summary>
public partial class ClientMain : Node
{
    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;
    [Export] private MainMenu _menu = null!;
    [Export] private Lobby _lobby = null!;

    private readonly ClientConnectionAttempt _connection = new(CONNECT_RETRIES);
    private readonly ClientSession _session = new();
    private ClientMatchBootstrap? _pendingMatch;
    private GameView? _gameView;
    private bool _spawnedLocalServer;
    private bool _autoReady;
    private bool _subscribed;

    public override void _Ready()
    {
        Subscribe();
        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect == null)
            return;
        _autoReady = true;
        string playerName = CmdArgs.GetValue("--name") ?? "";
        StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT), playerName);
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        _connection.Cancel();
        ServerLauncher.Kill();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            ServerLauncher.Kill();
    }

    public void OnHostRequested(int port, string playerName, string adminPassword)
    {
        if (!ServerLauncher.Spawn(port, adminPassword))
        {
            _menu.SetStatus("Failed to start local server.");
            return;
        }
        _spawnedLocalServer = true;
        StartConnecting("127.0.0.1", port, playerName);
    }

    public void OnJoinRequested(string address, int port, string playerName) =>
        StartConnecting(address, port, playerName);

    public void OnReadyToggled(bool ready) => new SetReadyMsg(ready).SendToServer();

    private void Subscribe()
    {
        NetworkManager network = NetworkManager.Instance;
        network.Connected += OnConnected;
        network.ConnectionFailed += OnConnectionFailed;
        network.Disconnected += OnDisconnected;
        LobbyStateMsg.Received += OnLobbyState;
        WelcomeMsg.Received += OnWelcome;
        TerrainChunkMsg.Received += OnTerrainChunk;
        MatchEndMsg.Received += OnMatchEnd;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        NetworkManager network = NetworkManager.Instance;
        network.Connected -= OnConnected;
        network.ConnectionFailed -= OnConnectionFailed;
        network.Disconnected -= OnDisconnected;
        LobbyStateMsg.Received -= OnLobbyState;
        WelcomeMsg.Received -= OnWelcome;
        TerrainChunkMsg.Received -= OnTerrainChunk;
        MatchEndMsg.Received -= OnMatchEnd;
        _subscribed = false;
    }

    private void StartConnecting(string address, int port, string playerName)
    {
        _connection.Start(address, port, playerName);
        _session.BeginConnecting();
        _pendingMatch = null;
        _menu.SetStatus($"Connecting to {address}:{port}...");
        GD.Print($"[client] connecting to {address}:{port}");
        TryConnect();
    }

    private void TryConnect()
    {
        NetworkManager.Instance.ResetPeer();
        Error error = NetworkManager.Instance.StartClient(_connection.Address, _connection.Port);
        if (error != Error.Ok)
            OnConnectionFailed();
    }

    private async void OnConnectionFailed()
    {
        ConnectionFailure failure = _connection.Failed();
        if (failure.Action == ConnectionFailureAction.Ignore)
            return;
        if (failure.Action == ConnectionFailureAction.Retry)
        {
            _menu.SetStatus($"Retrying... ({failure.RetryNumber}/{failure.MaxRetries})");
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            if (_connection.BeginScheduledRetry(failure.Generation))
                TryConnect();
            return;
        }

        GD.Print("[client] connection failed");
        NetworkManager.Instance.ResetPeer();
        ReturnToMenu("Connection failed.", stopLocalServer: true);
    }

    private void OnConnected()
    {
        _connection.Connected();
        GD.Print($"[client] connected, peer id {Multiplayer.GetUniqueId()}");
        NetworkManager.Instance.SendHello(_connection.PlayerName);
        _menu.SetStatus("Entering lobby...");
        if (_autoReady)
            new SetReadyMsg(true).SendToServer();
    }

    private void OnLobbyState(LobbyStateMsg message)
    {
        bool returningFromMatch = _session.Stage is
            ClientSessionStage.LoadingMatch or ClientSessionStage.Playing;
        if (!_session.TryEnterLobby())
            return;
        if (returningFromMatch)
        {
            Engine.TimeScale = 1;
            DisposeGameView();
            _pendingMatch = null;
            _lobby.ResetLocalReady();
            if (_autoReady)
                new SetReadyMsg(true).SendToServer();
        }
        _menu.Visible = false;
        _lobby.Visible = true;
        _lobby.UpdatePlayers(message.PeerIds, message.Names, message.ReadyFlags,
            Multiplayer.GetUniqueId());
    }

    private void OnWelcome(WelcomeMsg message)
    {
        if (!_session.TryBeginMatchLoad())
            return;
        if (!ClientMatchBootstrap.TryCreate(message, out ClientMatchBootstrap? bootstrap,
                out string error))
        {
            GD.PrintErr($"[client] {error}, disconnecting");
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
        if (result.State is TerrainChunkState.Ignored or TerrainChunkState.Waiting)
            return;
        if (result.State == TerrainChunkState.Rejected)
        {
            RejectWelcome(result.Error);
            return;
        }

        EnterMatch(pending, result.Data!);
    }

    private void EnterMatch(ClientMatchBootstrap bootstrap, byte[] terrainData)
    {
        if (!_session.TryEnterMatch())
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

        _menu.Visible = false;
        _lobby.Visible = false;
        _gameView = gameView;
        AddChild(gameView);
        _pendingMatch = null;
    }

    private void RejectWelcome(string reason)
    {
        GD.PrintErr($"[client] {reason} Disconnecting.");
        _connection.Cancel();
        NetworkManager.Instance.ResetPeer();
        ReturnToMenu(reason, stopLocalServer: true);
    }

    private void OnMatchEnd(MatchEndMsg message)
    {
        if (_session.CanEnterSlowMotion)
            Engine.TimeScale = SimConfig.MATCH_END_TIME_SCALE;
    }

    private void OnDisconnected()
    {
        GD.Print("[client] disconnected from server");
        _connection.Cancel();
        NetworkManager.Instance.ResetPeer();
        ReturnToMenu("Disconnected.", stopLocalServer: true);
    }

    private void ReturnToMenu(string status, bool stopLocalServer)
    {
        Engine.TimeScale = 1;
        DisposeGameView();
        _pendingMatch = null;
        _session.ReturnToMenu();
        _lobby.Visible = false;
        _lobby.ResetLocalReady();
        _menu.Visible = true;
        _menu.ShowHome();
        _menu.SetStatus(status);
        if (stopLocalServer && _spawnedLocalServer)
        {
            ServerLauncher.Kill();
            _spawnedLocalServer = false;
        }
    }

    private void DisposeGameView()
    {
        _gameView?.QueueFree();
        _gameView = null;
    }
}
