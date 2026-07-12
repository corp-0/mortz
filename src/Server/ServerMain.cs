using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Server;

/// <summary>
/// Dedicated server: owns the authoritative SimWorld, steps it at the fixed
/// physics tick (60 Hz) and broadcasts snapshots. Terrain changes go out as
/// carve events. Runs headless; never instantiates any client/UI code.
/// </summary>
public partial class ServerMain : Node
{
    /// <summary>Map to load when --map is not passed.</summary>
    [Export] private string _defaultMap = "castlewars";

    private static readonly bool _netStats = CmdArgs.HasFlag("--net-stats");

    private SimWorld _sim = null!;
    private MapPackage _map = null!;

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
        _sim = new SimWorld(map.BuildMask(), Random.Shared.Next());

        NetworkManager net = NetworkManager.Instance;
        net.PeerJoined += OnPeerJoined;
        net.PeerLeft += OnPeerLeft;
        net.InputsReceived += OnInputsReceived;
        net.DebugCarveRequested += OnDebugCarveRequested;

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
        _sim.Step();
        foreach ((int x, int y, int radius, int owner, int spawnSeq) in _sim.Explosions)
        {
            GD.Print($"[server] mortar exploded at ({x},{y})");
            NetworkManager.Instance.BroadcastCarve(x, y, radius, owner, spawnSeq);
        }
        foreach ((int peerId, Vec2 pos) in _sim.Deaths)
        {
            GD.Print($"[server] player {peerId} gibbed at ({(int)pos.X},{(int)pos.Y})");
            NetworkManager.Instance.BroadcastDeath(peerId, (int)pos.X, (int)pos.Y);
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

    private void OnPeerJoined(long peerId)
    {
        _sim.AddPlayer((int)peerId);
        NetworkManager.Instance.SendWelcome(peerId, _map.MapId, _map.Hash, _sim.Terrain.SerializeRemoved());
        GD.Print($"[server] player {peerId} joined ({_sim.Players.Count} in game)");
    }

    private void OnPeerLeft(long peerId)
    {
        _sim.RemovePlayer((int)peerId);
        GD.Print($"[server] player {peerId} left ({_sim.Players.Count} in game)");
    }

    private void OnInputsReceived(long peerId, byte[] packet)
    {
        foreach ((int seq, PlayerInput input) in InputPacket.Decode(packet))
            _sim.EnqueueInput((int)peerId, seq, input);
    }

    private void OnDebugCarveRequested(long peerId, int x, int y)
    {
        List<(int X, int Y)> removed = _sim.Terrain.CarveCircle(x, y, SimConfig.DEBUG_CARVE_RADIUS);
        GD.Print($"[server] carve at ({x},{y}) by {peerId}: {removed.Count} px");
        if (removed.Count > 0)
            NetworkManager.Instance.BroadcastCarve(x, y, SimConfig.DEBUG_CARVE_RADIUS, 0, -1);
    }
}
