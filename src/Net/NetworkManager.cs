using Godot;
using Mortz.Core.Input;
using Mortz.Core.Net;
using Mortz.Shared;

namespace Mortz.Net;

/// <summary>
/// Autoload owning the ENet peer: connection lifecycle, peer validation
/// (Hello), and the envelope every generated [NetMessage] rides. The only
/// bespoke RPCs are the hot path (inputs up, snapshots down) and Hello
/// itself.
/// </summary>
public partial class NetworkManager : Node, INetwork
{
    /// <summary>Composition roots resolve the autoload here.</summary>
    public const string AUTOLOAD_PATH = "/root/NetworkManager";

    /// <summary>Server side: a peer connected AND passed the protocol/schema check.</summary>
    [Signal] public delegate void PeerJoinedEventHandler(long peerId, string playerName);
    [Signal] public delegate void PeerLeftEventHandler(long peerId);
    [Signal] public delegate void InputsReceivedEventHandler(long peerId, byte[] packet);

    /// <summary>Client side.</summary>
    public event Action? Connected;
    public event Action? ConnectionFailed;
    public event Action? Disconnected;
    public event Action? TransportReset;
    /// <summary>ack = newest input sequence the server applied for THIS client.</summary>
    public event Action<byte[], int>? SnapshotReceived;

    private readonly PeerAdmissionState _admission = new();
    // Normal traffic is ~30 input datagrams/s and only occasional messages.
    // These bursts tolerate jitter while bounding work from any one peer.
    private readonly PeerRateLimiter _inputLimiter = new(capacity: 120, tokensPerSecond: 60);
    private readonly PeerRateLimiter _messageLimiter = new(capacity: 64, tokensPerSecond: 32);

    // Artificial latency for netcode testing (client side, `--fake-lag <ms>`):
    // outgoing and incoming packets are each held for half the lag. Covers the
    // hot path and every enveloped message.
    private int _fakeLagMs;
    private readonly Queue<(ulong Due, byte[] Packet)> _delayedInputs = new();
    private readonly Queue<(ulong Due, byte[] Data, int Ack)> _delayedSnapshots = new();
    private readonly Queue<(ulong Due, int MsgId, byte[] Payload, long Target, NetChannel Channel)> _delayedOutMsgs = new();
    private readonly Queue<(ulong Due, int MsgId, long Sender, byte[] Payload)> _delayedInMsgs = new();

    public bool IsServer => Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

    /// <summary>Safe at any time; 0 means no session (no real peer ever has id 0).</summary>
    public int LocalPeerId => Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;

    public override void _Ready()
    {
        NetTransport.Send = SendEnvelope;
        _fakeLagMs = CmdArgs.GetInt("--fake-lag", 0);
        if (_fakeLagMs > 0)
            GD.Print($"[net] simulating {_fakeLagMs} ms round-trip latency");
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += () => Connected?.Invoke();
        Multiplayer.ConnectionFailed += () => ConnectionFailed?.Invoke();
        Multiplayer.ServerDisconnected += () => Disconnected?.Invoke();
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
        _admission.Reset();
        _inputLimiter.Reset();
        _messageLimiter.Reset();
        TransportReset?.Invoke();
    }

    private void OnPeerConnected(long id)
    {
        // Server waits for Hello before considering the peer part of the game.
        if (IsServer)
            _admission.Connected(id, Time.GetTicksMsec());
        GD.Print($"[net] peer {id} connected");
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"[net] peer {id} disconnected");
        _inputLimiter.Remove(id);
        _messageLimiter.Remove(id);
        if (_admission.Remove(id))
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
        if (!_admission.TryValidate(sender))
        {
            GD.Print($"[net] peer {sender} rejected: duplicate or unsolicited Hello");
            Multiplayer.MultiplayerPeer.DisconnectPeer((int)sender);
            return;
        }
        EmitSignal(SignalName.PeerJoined, sender, PlayerNameSanitizer.Sanitize(playerName));
    }

    // ---- message envelope (everything generated rides here) ----

    private void SendEnvelope(ushort msgId, byte[] payload, long target, NetChannel channel)
    {
        if (payload.Length > NetConfig.MAX_ENVELOPE_BYTES)
        {
            GD.PrintErr($"[net] refused oversized outgoing message id {msgId} ({payload.Length} bytes)");
            return;
        }
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
            foreach (long peer in _admission.ValidatedPeers)
            {
                RpcId(peer, endpoint, msgId, payload);
            }
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
        if (payload.Length > NetConfig.MAX_ENVELOPE_BYTES || msgId is < 0 or > ushort.MaxValue)
            return;
        if (IsServer)
        {
            if (!_admission.IsValidated(sender) ||
                !_messageLimiter.Allow(sender, Time.GetTicksMsec(), NetAbusePolicy.EnvelopeCost(payload.Length)))
                return;
        }
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
        if (!_admission.IsValidated(sender) ||
            !_inputLimiter.Allow(sender, Time.GetTicksMsec()))
            return;
        if (InputPacket.TryDecode(packet, out _))
            EmitSignal(SignalName.InputsReceived, sender, packet);
    }

    // ---- hot path: snapshots down ----

    /// <summary>Each peer gets a snapshot with its own full prediction record;
    /// other players are compact render-only records.</summary>
    public int BroadcastSnapshot(Func<long, byte[]> dataFor, Func<long, int> ackFor)
    {
        int payloadBytes = 0;
        foreach (long peer in _admission.ValidatedPeers)
        {
            byte[] data = dataFor(peer);
            payloadBytes += data.Length + sizeof(int); // app payload incl. ack
            RpcId(peer, MethodName.ReceiveSnapshot, data, ackFor(peer));
        }
        return payloadBytes;
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] data, int ack)
    {
        if (_fakeLagMs > 0)
            _delayedSnapshots.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), data, ack));
        else
            SnapshotReceived?.Invoke(data, ack);
    }

    /// <summary>Server side: ENet's smoothed round-trip time per validated peer.
    /// Transport-level, so `--fake-lag` does not show up in it.</summary>
    public (long PeerId, int PingMs)[] PeerPingsMs()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enet)
            return [];
        return _admission.ValidatedPeers
            .Select(peerId => (peerId, (int)enet.GetPeer((int)peerId)
                .GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime)))
            .ToArray();
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
        ulong now = Time.GetTicksMsec();
        if (IsServer)
        {
            foreach (long peerId in _admission.Expire(now))
            {
                GD.Print($"[net] peer {peerId} rejected: Hello timeout");
                Multiplayer.MultiplayerPeer.DisconnectPeer((int)peerId);
            }
        }
        if (_fakeLagMs <= 0)
            return;
        while (_delayedInputs.Count > 0 && _delayedInputs.Peek().Due <= now)
        {
            RpcId(1, MethodName.SubmitInputs, _delayedInputs.Dequeue().Packet);
        }
        while (_delayedSnapshots.Count > 0 && _delayedSnapshots.Peek().Due <= now)
        {
            (ulong _, byte[] data, int ack) = _delayedSnapshots.Dequeue();
            SnapshotReceived?.Invoke(data, ack);
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
