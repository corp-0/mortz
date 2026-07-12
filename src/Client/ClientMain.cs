using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Client flow: lobby (Host / Join by IP) -> connect -> hand over to GameView.
/// Also supports headless auto-join for E2E testing: `++ --connect 127.0.0.1`.
/// </summary>
public partial class ClientMain : Node
{
    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;

    private Control _lobby = null!;
    private LineEdit _addressEdit = null!;
    private Label _status = null!;
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

        BuildLobbyUi();

        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect != null)
            StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
            ServerLauncher.Kill();
    }

    // ---- lobby ----

    private void BuildLobbyUi()
    {
        _lobby = new CenterContainer();
        _lobby.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        VBoxContainer box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        box.AddThemeConstantOverride("separation", 12);
        _lobby.AddChild(box);

        Label title = new Label { Text = "MORTZ", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        box.AddChild(title);

        Button hostButton = new Button { Text = "Host game" };
        hostButton.Pressed += OnHostPressed;
        box.AddChild(hostButton);

        _addressEdit = new LineEdit { Text = "127.0.0.1", PlaceholderText = "server address" };
        box.AddChild(_addressEdit);

        Button joinButton = new Button { Text = "Join" };
        joinButton.Pressed += () => StartConnecting(_addressEdit.Text.Trim(), NetConfig.DEFAULT_PORT);
        box.AddChild(joinButton);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_status);

        AddChild(_lobby);
    }

    private void OnHostPressed()
    {
        int port = NetConfig.DEFAULT_PORT;
        if (!ServerLauncher.Spawn(port))
        {
            _status.Text = "Failed to start local server.";
            return;
        }
        _spawnedLocalServer = true;
        StartConnecting("127.0.0.1", port);
    }

    // ---- connection ----

    private void StartConnecting(string address, int port)
    {
        _pendingAddress = address;
        _pendingPort = port;
        _retriesLeft = CONNECT_RETRIES;
        _status.Text = $"Connecting to {address}:{port}...";
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
            _status.Text = $"Retrying... ({CONNECT_RETRIES - _retriesLeft}/{CONNECT_RETRIES})";
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            TryConnect();
            return;
        }
        GD.Print("[client] connection failed");
        _status.Text = "Connection failed.";
        NetworkManager.Instance.ResetPeer();
    }

    private void OnConnected()
    {
        GD.Print($"[client] connected, peer id {Multiplayer.GetUniqueId()}");
        NetworkManager.Instance.SendHello();
        _status.Text = "Loading map...";
    }

    private void OnWelcomeReceived(string mapId, string mapHash, byte[] removedData)
    {
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null || map.Hash != mapHash)
        {
            GD.PrintErr($"[client] map '{mapId}' missing or mismatched with server, disconnecting");
            NetworkManager.Instance.ResetPeer();
            _status.Text = $"Map mismatch: {mapId}";
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
        _status.Text = "Disconnected.";
    }
}
