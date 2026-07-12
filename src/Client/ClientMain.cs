using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Client session flow: lobby (Host / Join by IP) -> connect -> verify map ->
/// hand over to GameView. Also supports headless auto-join for E2E testing:
/// `++ --connect 127.0.0.1`.
/// </summary>
public partial class ClientMain : Node
{
    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;
    [Export] private Lobby _lobby = null!;

    private GameView? _gameView;
    private bool _spawnedLocalServer;
    private int _retriesLeft;
    private string _pendingAddress = "";
    private int _pendingPort;

    public override void _Ready()
    {
        NetworkManager net = NetworkManager.Instance;
        net.Connected += OnConnected;
        net.ConnectionFailed += OnConnectionFailed;
        net.Disconnected += OnDisconnected;
        net.WelcomeReceived += OnWelcomeReceived;

        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect != null)
            StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
            ServerLauncher.Kill();
    }

    // ---- lobby intents (connected in ClientMain.tscn) ----

    public void OnHostRequested()
    {
        int port = NetConfig.DEFAULT_PORT;
        if (!ServerLauncher.Spawn(port))
        {
            _lobby.SetStatus("Failed to start local server.");
            return;
        }
        _spawnedLocalServer = true;
        StartConnecting("127.0.0.1", port);
    }

    public void OnJoinRequested(string address) => StartConnecting(address, NetConfig.DEFAULT_PORT);

    // ---- connection ----

    private void StartConnecting(string address, int port)
    {
        _pendingAddress = address;
        _pendingPort = port;
        _retriesLeft = CONNECT_RETRIES;
        _lobby.SetStatus($"Connecting to {address}:{port}...");
        GD.Print($"[client] connecting to {address}:{port}");
        TryConnect();
    }

    private void TryConnect()
    {
        NetworkManager.Instance.ResetPeer();
        Error err = NetworkManager.Instance.StartClient(_pendingAddress, _pendingPort);
        if (err != Error.Ok)
            OnConnectionFailed();
    }

    private async void OnConnectionFailed()
    {
        // A freshly spawned local server takes a moment to start listening.
        if (_retriesLeft-- > 0)
        {
            _lobby.SetStatus($"Retrying... ({CONNECT_RETRIES - _retriesLeft}/{CONNECT_RETRIES})");
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            TryConnect();
            return;
        }
        GD.Print("[client] connection failed");
        _lobby.SetStatus("Connection failed.");
        NetworkManager.Instance.ResetPeer();
    }

    private void OnConnected()
    {
        GD.Print($"[client] connected, peer id {Multiplayer.GetUniqueId()}");
        NetworkManager.Instance.SendHello();
        _lobby.SetStatus("Loading map...");
    }

    private void OnWelcomeReceived(string mapId, string mapHash, byte[] removedData)
    {
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null || map.Hash != mapHash)
        {
            GD.PrintErr($"[client] map '{mapId}' missing or mismatched with server, disconnecting");
            NetworkManager.Instance.ResetPeer();
            _lobby.SetStatus($"Map mismatch: {mapId}");
            return;
        }
        GD.Print($"[client] map '{map.DisplayName}' verified");

        _lobby.Visible = false;
        _gameView = _gameViewScene.Instantiate<GameView>();
        _gameView.Initialize(map, removedData);
        AddChild(_gameView);
    }

    private void OnDisconnected()
    {
        GD.Print("[client] disconnected from server");
        _gameView?.QueueFree();
        _gameView = null;
        NetworkManager.Instance.ResetPeer();
        if (_spawnedLocalServer)
        {
            ServerLauncher.Kill();
            _spawnedLocalServer = false;
        }
        _lobby.Visible = true;
        _lobby.SetStatus("Disconnected.");
    }
}
