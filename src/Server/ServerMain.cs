using Godot;
using Mortz.Core;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>
/// Dedicated server: owns the authoritative SimWorld, steps it at the fixed
/// physics tick (60 Hz) and broadcasts snapshots. Terrain changes go out as
/// carve events. Runs headless; never instantiates any client/UI code.
/// Peers first gather in a pre-match lobby; the match starts once everyone
/// there is ready. Anyone connecting after that drops straight into the game.
/// </summary>
public partial class ServerMain : Node
{
    private enum Phase
    {
        LOBBY,
        IN_GAME
    }

    /// <summary>Map to load when --map is not passed.</summary>
    [Export] private string _defaultMap = "castlewars";

    private static readonly bool _netStats = CmdArgs.HasFlag("--net-stats");

    private SimWorld _sim = null!;
    private MapPackage _map = null!;
    private Phase _phase = Phase.LOBBY;
    private readonly Dictionary<long, bool> _lobbyReady = new();
    private readonly Dictionary<long, string> _names = new();

    /// <summary>Grants live lobby control via /admin
    ///  Empty = no admin access. Never logged.</summary>
    private string _adminPassword = "";

    public override void _Ready()
    {
        string mapId = CmdArgs.GetValue("--map") ?? _defaultMap;
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            GetTree().Quit(1);
            return;
        }
        _map = map;
        MatchConfig? config = LoadRuleset();
        ServerConfig? serverConfig = ServerConfig.Load();
        if (config == null || serverConfig == null)
        {
            GetTree().Quit(1);
            return;
        }
        _adminPassword = CmdArgs.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (_adminPassword.Length > 0)
            GD.Print("[server] admin password set");
        _sim = new SimWorld(map.BuildMask(), config, Random.Shared.Next());

        NetworkManager net = NetworkManager.Instance;
        net.PeerJoined += OnPeerJoined;
        net.PeerLeft += OnPeerLeft;
        net.InputsReceived += OnInputsReceived;
        SetReadyMsg.Received += OnSetReady;
        DebugCarveMsg.Received += OnDebugCarve;

        int port = CmdArgs.GetInt("--port", NetConfig.DEFAULT_PORT);
        Error err = net.StartServer(port);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[server] failed to listen on port {port}: {err}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"[server] listening on port {port} (protocol v{NetConfig.PROTOCOL_VERSION}, " +
                 $"map '{map.DisplayName}' {map.Width}x{map.Height}, tick {SimConfig.TICK_RATE} Hz)");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.LOBBY)
            return;
        _sim.Step();
        foreach ((int x, int y, int radius, int owner, int spawnSeq) in _sim.Explosions)
        {
            GD.Print($"[server] mortar exploded at ({x},{y})");
            new CarveMsg(x, y, radius, owner, spawnSeq).Broadcast();
        }
        foreach ((int peerId, Vec2 pos, bool owned) in _sim.Deaths)
        {
            GD.Print($"[server] player {peerId} gibbed at ({(int)pos.X},{(int)pos.Y})" +
                     (owned ? " (OWNED)" : ""));
            new DeathMsg(peerId, (int)pos.X, (int)pos.Y, owned).Broadcast();
        }
        if (_sim.Tick % NetConfig.TICKS_PER_SNAPSHOT == 0 && _sim.Players.Count > 0)
        {
            byte[] snapshot = _sim.TakeSnapshot().Serialize();
            NetworkManager.Instance.BroadcastSnapshot(snapshot,
                peerId => _sim.Players.TryGetValue((int)peerId, out PlayerState p) ? p.LastInputSeq : -1);
        }
        if (_netStats && _sim.Tick % SimConfig.TICK_RATE == 0)
            PrintNetStats();
    }

    // One line per second: wire totals from ENet plus each player's input
    // backlog. pending > 1 means standing input latency, one tick per input.
    private void PrintNetStats()
    {
        (double sent, double recv, double sentPk, double recvPk) = NetworkManager.Instance.PopWireStats();
        string peers = string.Join(" ", _sim.Players.Keys.Select(
            id => $"peer={id} pending={_sim.PendingInputs(id)} ack={_sim.Players[id].LastInputSeq}"));
        GD.Print($"[stats] unix={Time.GetUnixTimeFromSystem():F3} tick={_sim.Tick} " +
                 $"sent={sent:F0}B/{sentPk:F0}pk recv={recv:F0}B/{recvPk:F0}pk {peers}");
    }

    private void OnPeerJoined(long peerId, string playerName)
    {
        playerName = playerName.Trim();
        if (playerName.Length == 0)
            playerName = $"Player {peerId}";
        else if (playerName.Length > NetConfig.MAX_NAME_LENGTH)
            playerName = playerName[..NetConfig.MAX_NAME_LENGTH];
        _names[peerId] = playerName;

        if (_phase == Phase.IN_GAME)
        {
            AddToGame(peerId);
            BroadcastRoster(); // after Welcome, so the joiner's GameView is listening
            return;
        }
        _lobbyReady[peerId] = false;
        GD.Print($"[server] player {peerId} '{playerName}' entered lobby ({_lobbyReady.Count} waiting)");
        BroadcastLobbyState();
    }

    private void OnPeerLeft(long peerId)
    {
        _names.Remove(peerId);
        if (_lobbyReady.Remove(peerId))
        {
            GD.Print($"[server] player {peerId} left lobby ({_lobbyReady.Count} waiting)");
            BroadcastLobbyState();
            TryStartMatch(); // the leaver may have been the only one not ready
            return;
        }
        _sim.RemovePlayer((int)peerId);
        GD.Print($"[server] player {peerId} left ({_sim.Players.Count} in game)");
        BroadcastRoster();
    }

    private void OnSetReady(long sender, SetReadyMsg msg)
    {
        if (!_lobbyReady.ContainsKey(sender))
            return;
        _lobbyReady[sender] = msg.Ready;
        GD.Print($"[server] player {sender} is {(msg.Ready ? "ready" : "not ready")}");
        BroadcastLobbyState();
        TryStartMatch();
    }

    private void BroadcastLobbyState()
    {
        long[] peers = _lobbyReady.Keys.ToArray();
        string[] names = peers.Select(p => _names[p]).ToArray();
        byte[] flags = peers.Select(p => _lobbyReady[p] ? (byte)1 : (byte)0).ToArray();
        new LobbyStateMsg(peers, names, flags).Broadcast();
    }

    private void TryStartMatch()
    {
        if (_phase != Phase.LOBBY || _lobbyReady.Count == 0 || _lobbyReady.ContainsValue(false))
            return;
        _phase = Phase.IN_GAME;
        GD.Print($"[server] all {_lobbyReady.Count} player(s) ready, starting match");
        foreach (long peerId in _lobbyReady.Keys)
            AddToGame(peerId);
        _lobbyReady.Clear();
        BroadcastRoster(); // lobby-phase rosters would predate everyone's GameView
    }

    /// <summary>Name list for in-game display (nameplates); only sent while IN_GAME,
    /// the lobby gets names through the lobby state instead.</summary>
    private void BroadcastRoster()
    {
        long[] peers = _names.Keys.ToArray();
        string[] names = peers.Select(p => _names[p]).ToArray();
        new RosterMsg(peers, names).Broadcast();
    }

    /// <summary>The host's ruleset preset (--ruleset path.json), or defaults
    /// without the flag. Null means the file was asked for but unusable; the
    /// host should hear about that, not silently run defaults.</summary>
    private static MatchConfig? LoadRuleset()
    {
        string? path = CmdArgs.GetValue("--ruleset");
        if (path == null)
            return new MatchConfig();
        try
        {
            MatchConfig config = MatchConfig.FromJson(File.ReadAllText(path));
            GD.Print($"[server] ruleset '{path}' loaded");
            return config;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[server] failed to load ruleset '{path}': {e.Message}");
            return null;
        }
    }

    private void AddToGame(long peerId)
    {
        _sim.AddPlayer((int)peerId);
        new WelcomeMsg(_map.MapId, _map.Hash, _sim.Config.ToBytes(), _sim.Terrain.SerializeRemoved()).SendTo(peerId);
        GD.Print($"[server] player {peerId} joined ({_sim.Players.Count} in game)");
    }

    private void OnInputsReceived(long peerId, byte[] packet)
    {
        foreach ((int seq, PlayerInput input) in InputPacket.Decode(packet))
            _sim.EnqueueInput((int)peerId, seq, input);
    }

    private void OnDebugCarve(long sender, DebugCarveMsg msg)
    {
        List<(int X, int Y)> removed = _sim.Terrain.CarveCircle(msg.X, msg.Y, SimConfig.DEBUG_CARVE_RADIUS);
        GD.Print($"[server] carve at ({msg.X},{msg.Y}) by {sender}: {removed.Count} px");
        if (removed.Count > 0)
            new CarveMsg(msg.X, msg.Y, SimConfig.DEBUG_CARVE_RADIUS, 0, -1).Broadcast();
    }
}
