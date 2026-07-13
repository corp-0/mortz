using Godot;
using Mortz.Core;
using Mortz.Core.Net;
using Mortz.Shared;

namespace Mortz.Net;

/// <summary>
/// Autoload owning the ENet peer. Pure transport glue: connection lifecycle,
/// peer validation (Hello), and the generic message envelope that every
/// generated [NetMessage] rides through. The only bespoke RPCs left are the
/// hot path (inputs up, snapshots down; per-peer acks and redundancy framing
/// don't fit a uniform envelope) and Hello itself, which is the validation
/// bootstrap the envelope relies on.
/// </summary>
public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; } = null!;

    /// <summary>Server side: a peer connected AND passed the protocol/schema check.</summary>
    [Signal] public delegate void PeerJoinedEventHandler(long peerId, string playerName);
    [Signal] public delegate void PeerLeftEventHandler(long peerId);
    [Signal] public delegate void InputsReceivedEventHandler(long peerId, byte[] packet);

    /// <summary>Client side.</summary>
    [Signal] public delegate void ConnectedEventHandler();
    [Signal] public delegate void ConnectionFailedEventHandler();
    [Signal] public delegate void DisconnectedEventHandler();
    /// <summary>ack = newest input sequence the server applied for THIS client.</summary>
    [Signal] public delegate void SnapshotReceivedEventHandler(byte[] data, int ack);

    // Peers that sent a valid Hello; only these take part in the game.
    private readonly HashSet<long> _validatedPeers = new();

    // Artificial latency for netcode testing (client side, `--fake-lag <ms>`):
    // outgoing and incoming packets are each held for half the lag. Covers the
    // hot path and every enveloped message.
    private int _fakeLagMs;
    private readonly Queue<(ulong Due, byte[] Packet)> _delayedInputs = new();
    private readonly Queue<(ulong Due, byte[] Data, int Ack)> _delayedSnapshots = new();
    private readonly Queue<(ulong Due, int MsgId, byte[] Payload, long Target, NetChannel Channel)> _delayedOutMsgs = new();
    private readonly Queue<(ulong Due, int MsgId, long Sender, byte[] Payload)> _delayedInMsgs = new();

    public bool IsServer => Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

    public override void _Ready()
    {
        Instance = this;
        NetTransport.Send = SendEnvelope;
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
        peer.Host.Compress(ENetConnection.CompressionMode.RangeCoder); // must match the client
        Multiplayer.MultiplayerPeer = peer;
        return Error.Ok;
    }

    public Error StartClient(string address, int port)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, port);
        if (err != Error.Ok)
            return err;
        peer.Host.Compress(ENetConnection.CompressionMode.RangeCoder); // must match the server
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

    // ---- validation bootstrap ----

    public void SendHello(string playerName) =>
        RpcId(1, MethodName.Hello, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, playerName);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocolVersion, ulong schemaHash, string playerName)
    {
        if (!IsServer) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (protocolVersion != NetConfig.PROTOCOL_VERSION || schemaHash != NetRegistry.SCHEMA_HASH)
        {
            GD.Print($"[net] peer {sender} rejected: protocol {protocolVersion}/{schemaHash:X16} " +
                     $"!= {NetConfig.PROTOCOL_VERSION}/{NetRegistry.SCHEMA_HASH:X16}");
            Multiplayer.MultiplayerPeer.DisconnectPeer((int)sender);
            return;
        }
        _validatedPeers.Add(sender);
        EmitSignal(SignalName.PeerJoined, sender, playerName);
    }

    // ---- message envelope (everything generated rides here) ----

    private void SendEnvelope(ushort msgId, byte[] payload, long target, NetChannel channel)
    {
        if (_fakeLagMs > 0)
        {
            _delayedOutMsgs.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), msgId, payload, target, channel));
            return;
        }
        SendEnvelopeNow(msgId, payload, target, channel);
    }

    private void SendEnvelopeNow(int msgId, byte[] payload, long target, NetChannel channel)
    {
        StringName endpoint = channel == NetChannel.RELIABLE ? MethodName.MsgReliable : MethodName.MsgUnreliable;
        if (target == NetTransport.BROADCAST)
        {
            foreach (long peer in _validatedPeers)
                RpcId(peer, endpoint, msgId, payload);
        }
        else
        {
            RpcId(target, endpoint, msgId, payload);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MsgReliable(int msgId, byte[] payload) => ReceiveEnvelope(msgId, payload);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void MsgUnreliable(int msgId, byte[] payload) => ReceiveEnvelope(msgId, payload);

    private void ReceiveEnvelope(int msgId, byte[] payload)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (IsServer && !_validatedPeers.Contains(sender))
            return;
        if (_fakeLagMs > 0)
        {
            _delayedInMsgs.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), msgId, sender, payload));
            return;
        }
        Dispatch(msgId, sender, payload);
    }

    private void Dispatch(int msgId, long sender, byte[] payload)
    {
        if (!NetRegistry.Dispatch((ushort)msgId, sender, payload, IsServer))
            GD.Print($"[net] dropped message id {msgId} from peer {sender} (unknown or wrong direction)");
    }

    // ---- hot path: inputs up ----

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

    // ---- hot path: snapshots down ----

    /// <summary>Same snapshot bytes to every validated peer, each with its own input ack.</summary>
    public void BroadcastSnapshot(byte[] data, Func<long, int> ackFor)
    {
        foreach (long peer in _validatedPeers)
            RpcId(peer, MethodName.ReceiveSnapshot, data, ackFor(peer));
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] data, int ack)
    {
        if (_fakeLagMs > 0)
            _delayedSnapshots.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), data, ack));
        else
            EmitSignal(SignalName.SnapshotReceived, data, ack);
    }

    /// <summary>
    /// Wire bytes/packets since the last call, from ENet's own counters, so
    /// the numbers include ENet framing and compression (not IP/UDP headers).
    /// </summary>
    public (double SentBytes, double RecvBytes, double SentPackets, double RecvPackets) PopWireStats()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer { Host: { } host })
            return default;
        return (
            host.PopStatistic(ENetConnection.HostStatistic.SentData),
            host.PopStatistic(ENetConnection.HostStatistic.ReceivedData),
            host.PopStatistic(ENetConnection.HostStatistic.SentPackets),
            host.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets));
    }

    public override void _Process(double delta)
    {
        if (_fakeLagMs <= 0)
            return;
        ulong now = Time.GetTicksMsec();
        while (_delayedInputs.Count > 0 && _delayedInputs.Peek().Due <= now)
            RpcId(1, MethodName.SubmitInputs, _delayedInputs.Dequeue().Packet);
        while (_delayedSnapshots.Count > 0 && _delayedSnapshots.Peek().Due <= now)
        {
            (ulong _, byte[] data, int ack) = _delayedSnapshots.Dequeue();
            EmitSignal(SignalName.SnapshotReceived, data, ack);
        }
        while (_delayedOutMsgs.Count > 0 && _delayedOutMsgs.Peek().Due <= now)
        {
            (ulong _, int msgId, byte[] payload, long target, NetChannel channel) = _delayedOutMsgs.Dequeue();
            SendEnvelopeNow(msgId, payload, target, channel);
        }
        while (_delayedInMsgs.Count > 0 && _delayedInMsgs.Peek().Due <= now)
        {
            (ulong _, int msgId, long sender, byte[] payload) = _delayedInMsgs.Dequeue();
            Dispatch(msgId, sender, payload);
        }
    }
}
