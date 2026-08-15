using Godot;
using Mortz.Core.Input;
using Mortz.Core.Net;
using Mortz.Core.Net.Abuse;
using Mortz.Core.Net.Names;
using Mortz.Core.Net.Stats;
using Mortz.Core.Sim;
using Mortz.Server;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
#if TOOLS
using Mortz.Shared.E2E;
#endif

namespace Mortz.Net;

/// <summary>Autoload owning the ENet peer: connection lifecycle, peer validation
/// (Hello), and the envelope every generated [NetMessage] rides.</summary>
public partial class NetworkManager : Node, INetwork, IClientSender
{
    private static readonly ILogger _log = MortzLog.For("net");

    /// <summary>Composition roots resolve the autoload here.</summary>
    public const string AUTOLOAD_PATH = "/root/NetworkManager";

    /// <summary>Server side: a peer connected AND passed the protocol/schema check.</summary>
    [Signal] public delegate void PeerJoinedEventHandler(int peerId, string playerName, int skin);
    [Signal] public delegate void PeerLeftEventHandler(int peerId);
    [Signal] public delegate void InputsReceivedEventHandler(int peerId, byte[] packet);

    /// <summary>Client side: the connection lifecycle.</summary>
    public event Action? Connected;
    public event Action? ConnectionFailed;
    public event Action? Disconnected;
    public event Action? TransportReset;
    /// <summary>ack = newest input sequence the server applied for THIS client.</summary>
    public event Action<byte[], int>? SnapshotReceived;

    /// <summary>Server side: every inbound envelope from a validated peer.</summary>
    public Action<int, ushort, byte[]>? ServerSink;

    /// <summary>Client side: every server-to-client message lands here, the
    /// mirror of the server's NetRouter&lt;Player&gt;.</summary>
    public NetRouter Router { get; } = new();

    private readonly HashSet<int> _undispatched = [];

#if TOOLS
    private readonly PeerGate _gate = new(rateScale: E2ELaunch.Timescale);
#else
    private readonly PeerGate _gate = new();
#endif

    // Artificial latency for netcode testing (client side, `--fake-lag <ms>`):
    // outgoing and incoming packets are each held for half the lag. Covers the
    // hot path and every enveloped message.
    private int _fakeLagMs;
    private readonly Queue<(ulong Due, byte[] Packet)> _delayedInputs = new();
    private readonly Queue<(ulong Due, byte[] Data, int Ack)> _delayedSnapshots = new();
    private readonly Queue<(ulong Due, int MsgId, byte[] Payload, int Target, NetChannel Channel)> _delayedOutMsgs = new();
    private readonly Queue<(ulong Due, int MsgId, int Sender, byte[] Payload)> _delayedInMsgs = new();

    public bool IsServer => Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

    /// <summary>Safe at any time; 0 means no session (no real peer ever has id 0).</summary>
    public int LocalPeerId => Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;

    public override void _Ready()
    {
        _fakeLagMs = CmdArgs.GetInt("--fake-lag", 0);
        if (_fakeLagMs > 0)
            _log.Information("simulating {LagMs} ms round-trip latency", _fakeLagMs);
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += () => Connected?.Invoke();
        Multiplayer.ConnectionFailed += () => ConnectionFailed?.Invoke();
        Multiplayer.ServerDisconnected += () => Disconnected?.Invoke();
    }

    public Error StartServer(int port)
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();
#if TOOLS
        // An E2E server is never reachable from off the machine.
        if (E2ELaunch.Enabled)
            peer.SetBindIP("127.0.0.1");
#endif
        Error err = peer.CreateServer(port, NetConfig.MAX_PLAYERS);
        if (err != Error.Ok)
            return err;
        peer.Host.Compress(ENetConnection.CompressionMode.RangeCoder); // must match the client
        // Without this the server forwards peer-to-peer RPCs, so a client could
        // deliver forged server messages to another client.
        ((SceneMultiplayer)Multiplayer).ServerRelay = false;
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
        _gate.Reset();
        TransportReset?.Invoke();
    }

    // Godot hands these ids over as long, the rest of the code uses int.
    private void OnPeerConnected(long id)
    {
        int peerId = (int)id;
        // Server waits for Hello before considering the peer part of the game.
        if (IsServer)
            _gate.Connected(peerId, Time.GetTicksMsec());
        _log.Information("peer {PeerId} connected", peerId);
    }

    private void OnPeerDisconnected(long id)
    {
        int peerId = (int)id;
        _log.Information("peer {PeerId} disconnected", peerId);
        if (_gate.Remove(peerId))
            EmitSignal(SignalName.PeerLeft, peerId);
    }

    public void SendHello(string playerName, int skin) =>
        RpcId(1, MethodName.Hello, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH,
            playerName, skin);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocolVersion, ulong schemaHash, string playerName, int skin)
    {
        if (!IsServer) return;
        int sender = Multiplayer.GetRemoteSenderId();
        if (protocolVersion != NetConfig.PROTOCOL_VERSION || schemaHash != NetRegistry.SCHEMA_HASH)
        {
            _log.Information(
                "peer {PeerId} rejected: protocol {Protocol}/{Schema:X16} != {OurProtocol}/{OurSchema:X16}",
                sender, protocolVersion, schemaHash, NetConfig.PROTOCOL_VERSION,
                NetRegistry.SCHEMA_HASH);
            Multiplayer.MultiplayerPeer.DisconnectPeer(sender);
            return;
        }
        if (skin is < 0 or >= SimConfig.SKIN_COUNT)
        {
            _log.Information("peer {PeerId} rejected: invalid skin {Skin}", sender, skin);
            Multiplayer.MultiplayerPeer.DisconnectPeer(sender);
            return;
        }
        if (!_gate.TryValidate(sender))
        {
            _log.Information("peer {PeerId} rejected: duplicate or unsolicited Hello", sender);
            Multiplayer.MultiplayerPeer.DisconnectPeer(sender);
            return;
        }
        EmitSignal(SignalName.PeerJoined, sender, PlayerNameSanitizer.Sanitize(playerName), skin);
    }

    public void SendEnvelope(ushort msgId, byte[] payload, int target, NetChannel channel)
    {
        if (payload.Length > NetConfig.MAX_ENVELOPE_BYTES)
        {
            _log.Error("refused oversized outgoing {MessageName} ({Bytes} bytes)",
                NetRegistry.NameOf(msgId),
                payload.Length);
            return;
        }
        if (_fakeLagMs > 0)
        {
            _delayedOutMsgs.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), msgId, payload, target, channel));
            return;
        }
        SendEnvelopeNow(msgId, payload, target, channel);
    }

    public void Send<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg> =>
        SendEnvelope(TMsg.MsgId, TMsg.Serialize(in message), NetConfig.SERVER_PEER_ID,
            TMsg.MsgChannel);

    private void SendEnvelopeNow(int msgId, byte[] payload, int target, NetChannel channel)
    {
        StringName endpoint = channel == NetChannel.RELIABLE ? MethodName.MsgReliable : MethodName.MsgUnreliable;
        if (target == NetConfig.BROADCAST_PEER_ID)
        {
            foreach (int peer in _gate.ValidatedPeers)
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
        int sender = Multiplayer.GetRemoteSenderId();
        if (payload.Length > NetConfig.MAX_ENVELOPE_BYTES || msgId is < 0 or > ushort.MaxValue)
            return;
        if (IsServer)
        {
            if (!_gate.IsValidated(sender) ||
                !_gate.AllowMessage(sender, Time.GetTicksMsec(), NetAbusePolicy.EnvelopeCost(payload.Length)))
                return;
        }
        else if (sender != NetConfig.SERVER_PEER_ID)
        {
            return;
        }
        if (_fakeLagMs > 0)
        {
            _delayedInMsgs.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), msgId, sender, payload));
            return;
        }
        Dispatch(msgId, sender, payload);
    }

    private void Dispatch(int msgId, int sender, byte[] payload)
    {
        // The server routes everything through the sink, which logs what it
        // cannot place; the client routes through its own NetRouter.
        if (IsServer)
        {
            ServerSink?.Invoke(sender, (ushort)msgId, payload);
            return;
        }
        if (Router.Dispatch((ushort)msgId, payload))
            return;
        // A client can legitimately race a phase change, so this is noise, not an error.
        if (_undispatched.Add(msgId))
            _log.Information("no handler for {MessageName}", NetRegistry.NameOf((ushort)msgId));
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
        int sender = Multiplayer.GetRemoteSenderId();
        if (!_gate.IsValidated(sender) ||
            !_gate.AllowInput(sender, Time.GetTicksMsec()))
            return;
        if (InputPacket.TryDecode(packet, out _))
            EmitSignal(SignalName.InputsReceived, sender, packet);
    }

    /// <summary>Server side: drop a peer without waiting for it to leave.</summary>
    public void Kick(int peerId) => Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);

    /// <summary>Each peer gets a snapshot with its own full prediction record;
    /// other players are compact render-only records.</summary>
    public int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor)
    {
        int payloadBytes = 0;
        foreach (int peer in _gate.ValidatedPeers)
        {
            byte[] data = dataFor(peer);
            payloadBytes += data.Length + sizeof(int); // app payload incl. ack
            RpcId(peer, MethodName.ReceiveSnapshot, data, ackFor(peer));
        }
        return payloadBytes;
    }

    public int SendSnapshot(int peerId, byte[] data, int ack)
    {
        RpcId(peerId, MethodName.ReceiveSnapshot, data, ack);
        return data.Length + sizeof(int);
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] data, int ack)
    {
        if (_fakeLagMs > 0)
            _delayedSnapshots.Enqueue((Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2), data, ack));
        else
            SnapshotReceived?.Invoke(data, ack);
    }

    /// <summary>The port ENet actually bound, so `--port 0` can be resolved;
    /// -1 when there is no ENet host.</summary>
    public int BoundPort() =>
        Multiplayer.MultiplayerPeer is ENetMultiplayerPeer { Host: { } host }
            ? host.GetLocalPort()
            : -1;

    /// <summary>Server side: ENet's smoothed round-trip time per validated peer.
    /// Transport-level, so `--fake-lag` does not show up in it.</summary>
    public PeerPing[] PeerPingsMs()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enet)
            return [];
        return _gate.ValidatedPeers
            .Select(peerId => new PeerPing(peerId, (int)enet.GetPeer(peerId)
                .GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime)))
            .ToArray();
    }

    /// <summary>
    /// Wire bytes/packets since the last call, from ENet's own counters, so
    /// the numbers include ENet framing and compression (not IP/UDP headers).
    /// </summary>
    public WireStats PopWireStats()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer { Host: { } host })
            return default;
        return new WireStats(
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
            foreach (int peerId in _gate.Expire(now))
            {
                _log.Information("peer {PeerId} rejected: Hello timeout", peerId);
                Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);
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
            (ulong _, int msgId, byte[] payload, int target, NetChannel channel) = _delayedOutMsgs.Dequeue();
            SendEnvelopeNow(msgId, payload, target, channel);
        }
        while (_delayedInMsgs.Count > 0 && _delayedInMsgs.Peek().Due <= now)
        {
            (ulong _, int msgId, int sender, byte[] payload) = _delayedInMsgs.Dequeue();
            Dispatch(msgId, sender, payload);
        }
    }
}
