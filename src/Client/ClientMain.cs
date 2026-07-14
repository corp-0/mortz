using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Client session flow: main menu (Host / Join) -> connect -> pre-match lobby
/// (ready up) -> verify map -> hand over to GameView. Also supports headless
/// auto-join for E2E testing: `++ --connect 127.0.0.1` (readies up
/// automatically).
/// </summary>
public partial class ClientMain : Node
{
    private sealed class PendingTerrain
    {
        public required MapPackage Map;
        public required MatchConfig Config;
        public required WelcomeMsg Welcome;
        public required byte[][] Chunks;
        public int Received;
    }

    private const int CONNECT_RETRIES = 5;

    [Export] private PackedScene _gameViewScene = null!;
    [Export] private MainMenu _menu = null!;
    [Export] private Lobby _lobby = null!;

    private GameView? _gameView;
    private bool _spawnedLocalServer;
    private bool _autoReady;
    private int _retriesLeft;
    private string _pendingAddress = "";
    private int _pendingPort;
    private string _playerName = "";
    private PendingTerrain? _pendingTerrain;

    public override void _Ready()
    {
        NetworkManager net = NetworkManager.Instance;
        net.Connected += OnConnected;
        net.ConnectionFailed += OnConnectionFailed;
        net.Disconnected += OnDisconnected;
        LobbyStateMsg.Received += OnLobbyState;
        WelcomeMsg.Received += OnWelcome;
        TerrainChunkMsg.Received += OnTerrainChunk;
        MatchEndMsg.Received += OnMatchEnd;

        string? autoConnect = CmdArgs.GetValue("--connect");
        if (autoConnect != null)
        {
            _autoReady = true;
            _playerName = CmdArgs.GetValue("--name") ?? ""; // empty: server assigns "Player <id>"
            StartConnecting(autoConnect, CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
            ServerLauncher.Kill();
    }

    // ---- menu and lobby intents (connected in ClientMain.tscn) ----

    public void OnHostRequested(int port, string playerName, string adminPassword)
    {
        if (!ServerLauncher.Spawn(port, adminPassword))
        {
            _menu.SetStatus("Failed to start local server.");
            return;
        }
        _spawnedLocalServer = true;
        _playerName = playerName;
        StartConnecting("127.0.0.1", port);
    }

    public void OnJoinRequested(string address, int port, string playerName)
    {
        _playerName = playerName;
        StartConnecting(address, port);
    }

    public void OnReadyToggled(bool ready) => new SetReadyMsg(ready).SendToServer();

    // ---- connection ----

    private void StartConnecting(string address, int port)
    {
        _pendingAddress = address;
        _pendingPort = port;
        _retriesLeft = CONNECT_RETRIES;
        _menu.SetStatus($"Connecting to {address}:{port}...");
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
            _menu.SetStatus($"Retrying... ({CONNECT_RETRIES - _retriesLeft}/{CONNECT_RETRIES})");
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            TryConnect();
            return;
        }
        GD.Print("[client] connection failed");
        _menu.SetStatus("Connection failed.");
        NetworkManager.Instance.ResetPeer();
    }

    private void OnConnected()
    {
        GD.Print($"[client] connected, peer id {Multiplayer.GetUniqueId()}");
        NetworkManager.Instance.SendHello(_playerName);
        _menu.SetStatus("Entering lobby...");
        if (_autoReady)
            new SetReadyMsg(true).SendToServer();
    }

    private void OnLobbyState(LobbyStateMsg msg)
    {
        if (_gameView != null)
        {
            // The match ended and the server reset to the lobby: drop the
            // old world, the next match arrives with a fresh Welcome.
            Engine.TimeScale = 1;
            _gameView.QueueFree();
            _gameView = null;
            _pendingTerrain = null;
            _lobby.ResetLocalReady();
            if (_autoReady)
                new SetReadyMsg(true).SendToServer();
        }
        _menu.Visible = false;
        _lobby.Visible = true;
        _lobby.UpdatePlayers(msg.PeerIds, msg.Names, msg.ReadyFlags, Multiplayer.GetUniqueId());
    }

    private void OnWelcome(WelcomeMsg msg)
    {
        MapPackage? map = MapPackage.Load(msg.MapId);
        if (map == null || map.Hash != msg.MapHash)
        {
            GD.PrintErr($"[client] map '{msg.MapId}' missing or mismatched with server, disconnecting");
            NetworkManager.Instance.ResetPeer();
            ShowMenu($"Map mismatch: {msg.MapId}");
            return;
        }
        GD.Print($"[client] map '{map.DisplayName}' verified");

        if (msg.TerrainEncoding > (byte)TerrainSyncEncoding.CarveLog ||
            msg.TerrainBytes is < 0 or > NetConfig.MAX_TERRAIN_SYNC_BYTES ||
            msg.TerrainChunks is < 1 or > NetConfig.MAX_TERRAIN_SYNC_CHUNKS)
        {
            RejectWelcome("Invalid terrain sync metadata.");
            return;
        }

        _pendingTerrain = new PendingTerrain
        {
            Map = map,
            Config = MatchConfig.FromBytes(msg.Config),
            Welcome = msg,
            Chunks = new byte[msg.TerrainChunks][],
        };
    }

    private void OnTerrainChunk(TerrainChunkMsg msg)
    {
        PendingTerrain? pending = _pendingTerrain;
        if (pending == null || msg.TransferId != pending.Welcome.TerrainTransferId)
            return;
        if (msg.Count != pending.Welcome.TerrainChunks || msg.Index < 0 || msg.Index >= msg.Count ||
            msg.Data.Length > NetConfig.TERRAIN_CHUNK_BYTES)
        {
            RejectWelcome("Invalid terrain sync chunk.");
            return;
        }
        if (pending.Chunks[msg.Index] != null)
            return;
        pending.Chunks[msg.Index] = msg.Data;
        pending.Received++;
        if (pending.Received != pending.Chunks.Length)
            return;

        byte[] data = new byte[pending.Welcome.TerrainBytes];
        int offset = 0;
        foreach (byte[] chunk in pending.Chunks)
        {
            if (offset + chunk.Length > data.Length)
            {
                RejectWelcome("Terrain sync length mismatch.");
                return;
            }
            Buffer.BlockCopy(chunk, 0, data, offset, chunk.Length);
            offset += chunk.Length;
        }
        if (offset != data.Length)
        {
            RejectWelcome("Terrain sync length mismatch.");
            return;
        }

        _menu.Visible = false;
        _lobby.Visible = false;
        GameView gameView = _gameViewScene.Instantiate<GameView>();
        try
        {
            gameView.Initialize(pending.Map, pending.Config,
                (TerrainSyncEncoding)pending.Welcome.TerrainEncoding, data);
        }
        catch (IOException e)
        {
            gameView.Free();
            RejectWelcome($"Invalid terrain sync: {e.Message}");
            return;
        }
        _gameView = gameView;
        AddChild(gameView);
        _pendingTerrain = null;
    }

    private void RejectWelcome(string reason)
    {
        GD.PrintErr($"[client] {reason} Disconnecting.");
        _pendingTerrain = null;
        NetworkManager.Instance.ResetPeer();
        ShowMenu(reason);
    }

    /// <summary>Slow-motion victory lap, same factor as the server so the
    /// tick streams stay in lockstep. Undone on the return to lobby.</summary>
    private void OnMatchEnd(MatchEndMsg msg) =>
        Engine.TimeScale = SimConfig.MATCH_END_TIME_SCALE;

    private void OnDisconnected()
    {
        GD.Print("[client] disconnected from server");
        Engine.TimeScale = 1;
        _gameView?.QueueFree();
        _gameView = null;
        _pendingTerrain = null;
        NetworkManager.Instance.ResetPeer();
        if (_spawnedLocalServer)
        {
            ServerLauncher.Kill();
            _spawnedLocalServer = false;
        }
        ShowMenu("Disconnected.");
    }

    private void ShowMenu(string status)
    {
        _lobby.Visible = false;
        _lobby.ResetLocalReady();
        _menu.Visible = true;
        _menu.ShowHome();
        _menu.SetStatus(status);
    }
}
