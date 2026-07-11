using Godot;
using Mortz.Core;
using Mortz.Shared;

namespace Mortz.Net;

/// <summary>
/// Autoload owning the ENet peer and every RPC endpoint. Pure transport glue:
/// no game decisions live here, it moves bytes/inputs between the wire and
/// whoever is listening (ServerMain or the client's GameView) via signals.
/// </summary>
public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    /// <summary>Server side: a peer connected AND passed the protocol-version check.</summary>
    [Signal] public delegate void PeerJoinedEventHandler(long peerId);
    [Signal] public delegate void PeerLeftEventHandler(long peerId);
    [Signal] public delegate void InputsReceivedEventHandler(long peerId, byte[] packet);

    [Signal] public delegate void DebugCarveRequestedEventHandler(long peerId, int x, int y);

    /// <summary>Client side.</summary>
    [Signal] public delegate void ConnectedEventHandler();
    [Signal] public delegate void ConnectionFailedEventHandler();
    [Signal] public delegate void DisconnectedEventHandler();
    [Signal] public delegate void SnapshotReceivedEventHandler(byte[] data);
    [Signal] public delegate void WelcomeReceivedEventHandler(string mapId, string mapHash, byte[] removedData);
    [Signal] public delegate void CarveReceivedEventHandler(int x, int y, int radius);

    // Peers that sent a valid Hello; only these take part in the game.
    private readonly HashSet<long> _validatedPeers = new();

    // Artificial latency for netcode testing (client side, `--fake-lag <ms>`):
    // outgoing inputs and incoming snapshots are each held for half the lag.
    private int _fakeLagMs;
    private readonly Queue<(ulong Due, byte[] Packet)> _delayedInputs = new();
    private readonly Queue<(ulong Due, byte[] Data)> _delayedSnapshots = new();

    public bool IsServer => Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

    public override void _Ready()
    {
        Instance = this;
        _fakeLagMs = CmdArgs.GetInt("--fake-lag", 0);
        if (_fakeLagMs > 0)
            GD.Print($"[net] simulating {_fakeLagMs} ms round-trip latency");
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += () => EmitSignal(SignalName.Connected);
        Multiplayer.ConnectionFailed += () => EmitSignal(SignalName.ConnectionFailed);
        Multiplayer.ServerDisconnected += () => EmitSignal(SignalName.Disconnected);
    }

    public Error StartServer(int port)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(port, NetConfig.MAX_PLAYERS);
        if (err != Error.Ok)
            return err;
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    public Error StartClient(string address, int port)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, port);
        if (err != Error.Ok)
            return err;
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    public void ResetPeer()
    {
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = null;
        _validatedPeers.Clear();
    }

    private void OnPeerConnected(long id)
    {
        // Server waits for Hello before considering the peer part of the game.
        GD.Print($"[net] peer {id} connected");
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"[net] peer {id} disconnected");
        if (_validatedPeers.Remove(id))
            EmitSignal(SignalName.PeerLeft, id);
    }

    // ---- client → server ----

    public void SendHello() => RpcId(1, MethodName.Hello, NetConfig.PROTOCOL_VERSION);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocolVersion)
    {
        if (!IsServer) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (protocolVersion != NetConfig.PROTOCOL_VERSION)
        {
            GD.Print($"[net] peer {sender} rejected: protocol {protocolVersion} != {NetConfig.PROTOCOL_VERSION}");
            Multiplayer.MultiplayerPeer.DisconnectPeer((int)sender);
            return;
        }
        _validatedPeers.Add(sender);
        EmitSignal(SignalName.PeerJoined, sender);
    }

    public void SendInputs(byte[] packet)
    {
        if (_fakeLagMs > 0)
            _delayedInputs.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), packet));
        else
            RpcId(1, MethodName.SubmitInputs, packet);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SubmitInputs(byte[] packet)
    {
        if (!IsServer) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (_validatedPeers.Contains(sender))
            EmitSignal(SignalName.InputsReceived, sender, packet);
    }

    public void RequestDebugCarve(int x, int y) => RpcId(1, MethodName.SubmitDebugCarve, x, y);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitDebugCarve(int x, int y)
    {
        if (!IsServer) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (_validatedPeers.Contains(sender))
            EmitSignal(SignalName.DebugCarveRequested, sender, x, y);
    }

    // ---- server → clients ----

    public void SendWelcome(long peerId, string mapId, string mapHash, byte[] removedData) =>
        RpcId(peerId, MethodName.Welcome, mapId, mapHash, removedData);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcome(string mapId, string mapHash, byte[] removedData) =>
        EmitSignal(SignalName.WelcomeReceived, mapId, mapHash, removedData);

    public void BroadcastCarve(int x, int y, int radius) => Rpc(MethodName.Carve, x, y, radius);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Carve(int x, int y, int radius) => EmitSignal(SignalName.CarveReceived, x, y, radius);

    public void BroadcastSnapshot(byte[] data) => Rpc(MethodName.ReceiveSnapshot, data);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] data)
    {
        if (_fakeLagMs > 0)
            _delayedSnapshots.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), data));
        else
            EmitSignal(SignalName.SnapshotReceived, data);
    }

    public override void _Process(double delta)
    {
        if (_fakeLagMs <= 0)
            return;
        ulong now = Time.GetTicksMsec();
        while (_delayedInputs.Count > 0 && _delayedInputs.Peek().Due <= now)
            RpcId(1, MethodName.SubmitInputs, _delayedInputs.Dequeue().Packet);
        while (_delayedSnapshots.Count > 0 && _delayedSnapshots.Peek().Due <= now)
            EmitSignal(SignalName.SnapshotReceived, _delayedSnapshots.Dequeue().Data);
    }
}
