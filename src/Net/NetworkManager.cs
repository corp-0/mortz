using Godot;
using Mortz.Client.Session;
using Mortz.Protocol.Input;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Abuse;
using Mortz.Protocol.Net.Admission;
using Mortz.Protocol.Net.Stats;
using Mortz.Server;
using Mortz.Server.Admission;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
#if TOOLS
using Mortz.Shared.E2E;
#endif

namespace Mortz.Net;

/// <summary>Owns ENet transport, reliable admission RPCs, and gated gameplay delivery.</summary>
[GlobalClass]
public partial class NetworkManager : Node, INetwork, IClientSender, IServerAdmissionTransport, IClientAdmissionTransport
{
    private static readonly ILogger _log = MortzLog.For("net");

    public event Action<int>? TransportPeerConnected;
    public event Action<int>? TransportPeerDisconnected;
    public event Action<int, AdmissionHello>? HelloReceived;
    public event Action<int, SteamProof>? ProofReceived;
    public event Action<AdmissionOffer>? OfferReceived;
    public event Action<AdmissionAccepted>? AdmissionAcceptedReceived;
    public event Action<AdmissionRejected>? AdmissionRejectedReceived;
    public bool ClientAdmitted { get; private set; }
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
    private readonly DelayedConnectionWork _delayed = new();

    public bool IsServer => IsInsideTree() && Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

    /// <summary>Safe at any time; 0 means no session (no real peer ever has id 0).</summary>
    public int LocalPeerId => IsInsideTree() && Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;

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

    public override void _ExitTree() => Shutdown();

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
        ClientAdmitted = false;
        Router.MatchGeneration = -1;
        _delayed.Reset();
        _undispatched.Clear();
        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = null;
        _gate.Reset();
        TransportReset?.Invoke();
    }

    public void Shutdown()
    {
        ClientAdmitted = false;
        Router.MatchGeneration = -1;
        _delayed.Reset();
        _undispatched.Clear();
        _gate.Reset();
        // Tree teardown removes RPC caches; detaching here races that cleanup in release templates.
        if (IsInsideTree() && Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer)
        {
            peer.Close();
        }
    }

    // Godot hands these ids over as long, the rest of the code uses int.
    private void OnPeerConnected(long id)
    {
        int peerId = (int)id;
        // Server waits for Hello before considering the peer part of the game.
        if (IsServer)
        {
            _gate.Connected(peerId);
            TransportPeerConnected?.Invoke(peerId);
        }
        _log.Information("peer {PeerId} connected", peerId);
    }

    private void OnPeerDisconnected(long id)
    {
        int peerId = (int)id;
        _log.Information("peer {PeerId} disconnected", peerId);
        _gate.Remove(peerId);
        if (IsServer)
        {
            TransportPeerDisconnected?.Invoke(peerId);
        }
    }

    void IClientAdmissionTransport.Hello(AdmissionHello hello) =>
        RpcId(NetConfig.SERVER_PEER_ID, MethodName.Hello, hello.ProtocolVersion, hello.SchemaHash,
            hello.Name, hello.Skin, hello.Steam);

    void IClientAdmissionTransport.Proof(SteamProof proof) =>
        RpcId(NetConfig.SERVER_PEER_ID, MethodName.SubmitSteamProof, proof.Attempt, proof.AccountId, proof.Ticket);

    void IServerAdmissionTransport.Offer(int peerId, AdmissionOffer offer) =>
        RpcId(peerId, MethodName.ReceiveAdmissionOffer, offer.Attempt, (int)offer.Mode, offer.ServerAccountId);

    void IServerAdmissionTransport.Accept(int peerId, AdmissionAccepted accepted)
    {
        RpcId(peerId, MethodName.ReceiveAdmissionAccepted, accepted.Attempt, (int)accepted.Mode);
        if (!_gate.TryValidate(peerId))
        {
            throw new InvalidOperationException("Only a pending connected peer can be admitted.");
        }
    }

    void IServerAdmissionTransport.Reject(int peerId, AdmissionRejected rejected)
    {
        _gate.Remove(peerId);
        RpcId(peerId, MethodName.ReceiveAdmissionRejected, rejected.Attempt, (int)rejected.Reason);
        // SceneMultiplayer disconnect drops queued RPCs; drain the reliable rejection first.
        ((ENetMultiplayerPeer)Multiplayer.MultiplayerPeer).GetPeer(peerId).PeerDisconnectLater();
    }

    public void MarkClientAdmitted() => ClientAdmitted = true;

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocolVersion, ulong schemaHash, string playerName, int skin, bool steam)
    {
        if (IsServer)
        {
            HelloReceived?.Invoke(Multiplayer.GetRemoteSenderId(),
                new AdmissionHello(protocolVersion, schemaHash, playerName, skin, steam));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitSteamProof(int attempt, ulong accountId, byte[]? ticket)
    {
        if (IsServer)
        {
            ProofReceived?.Invoke(Multiplayer.GetRemoteSenderId(), new SteamProof(attempt, accountId, ticket));
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAdmissionOffer(int attempt, int mode, ulong recipient)
    {
        if (!IsServer && Multiplayer.GetRemoteSenderId() == NetConfig.SERVER_PEER_ID)
        {
            OfferReceived?.Invoke(new AdmissionOffer(attempt, (AdmissionMode)mode, recipient));
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAdmissionAccepted(int attempt, int mode)
    {
        if (!IsServer && Multiplayer.GetRemoteSenderId() == NetConfig.SERVER_PEER_ID)
        {
            AdmissionAcceptedReceived?.Invoke(new AdmissionAccepted(attempt, (AdmissionMode)mode));
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveAdmissionRejected(int attempt, int reason)
    {
        if (!IsServer && Multiplayer.GetRemoteSenderId() == NetConfig.SERVER_PEER_ID)
        {
            ClientAdmitted = false;
            AdmissionRejectedReceived?.Invoke(new AdmissionRejected(attempt, (AdmissionRejection)reason));
        }
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
            _delayed.Schedule(Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2),
                () => SendEnvelopeNow(msgId, payload, target, channel));
            return;
        }
        SendEnvelopeNow(msgId, payload, target, channel);
    }

    public void Send<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        TMsg outgoing = TMsg.InMatch(message, Router.MatchGeneration ?? 0);
        SendEnvelope(TMsg.MsgId, TMsg.Serialize(outgoing), NetConfig.SERVER_PEER_ID,
            TMsg.MsgChannel);
    }

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
        else if (!IsServer || _gate.IsValidated(target))
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
        else if (!ClientAdmitted || sender != NetConfig.SERVER_PEER_ID)
        {
            return;
        }
        if (_fakeLagMs > 0)
        {
            _delayed.Schedule(Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2),
                () => Dispatch(msgId, sender, payload));
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
            if (!_gate.IsValidated(sender))
            {
                return;
            }
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
            _delayed.Schedule(Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2),
                () => RpcId(1, MethodName.SubmitInputs, packet));
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
    public void Kick(int peerId)
    {
        _gate.Remove(peerId);
        TransportPeerDisconnected?.Invoke(peerId);
        Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);
    }

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
        if (!_gate.IsValidated(peerId))
        {
            return 0;
        }
        RpcId(peerId, MethodName.ReceiveSnapshot, data, ack);
        return data.Length + sizeof(int);
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshot(byte[] data, int ack)
    {
        if (!ClientAdmitted || IsServer || Multiplayer.GetRemoteSenderId() != NetConfig.SERVER_PEER_ID)
        {
            return;
        }
        if (_fakeLagMs > 0)
            _delayed.Schedule(Time.GetTicksMsec() + (ulong)(_fakeLagMs / 2),
                () => SnapshotReceived?.Invoke(data, ack));
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
        if (_fakeLagMs <= 0)
            return;
        _delayed.Advance(now);
    }
}
