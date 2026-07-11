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
    private SimWorld _sim = null!;
    private MapPackage _map = null!;

    public override void _Ready()
    {
        string mapId = CmdArgs.GetValue("--map") ?? "arena01";
        MapPackage? map = MapPackage.Load(mapId);
        if (map == null)
        {
            GD.PrintErr($"[server] failed to load map '{mapId}'");
            GetTree().Quit(1);
            return;
        }
        _map = map;
        _sim = new SimWorld(map.BuildMask());

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
        if (_sim == null)
            return;
        _sim.Step();
        if (_sim.Tick % NetConfig.TICKS_PER_SNAPSHOT == 0 && _sim.Players.Count > 0)
            NetworkManager.Instance.BroadcastSnapshot(_sim.TakeSnapshot().Serialize());
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
            NetworkManager.Instance.BroadcastCarve(x, y, SimConfig.DEBUG_CARVE_RADIUS);
    }
}
