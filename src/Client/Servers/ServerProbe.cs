using System.Net;
using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Servers;

/// <summary>A hostname probe parked in Godot's resolver queue.</summary>
public readonly record struct PendingResolve(int ResolveId, ServerEndpoint Endpoint);

/// <summary>Sockets and DNS around ProbeTracker, which decides what a datagram means.</summary>
public partial class ServerProbe : Node
{
    private static readonly ILogger _log = MortzLog.For("browser");

    private const int MAX_PACKETS_PER_FRAME = 64;

    private readonly ProbeTracker _tracker = new();
    private readonly Dictionary<uint, PendingResolve> _resolves = [];
    private PacketPeerUdp? _socket;

    public event Action<ServerProbeReply>? Replied;

    public event Action<ServerEndpoint>? TimedOut;

    /// <summary>A server answered a broadcast we did not address to it.</summary>
    public event Action<ServerProbeReply>? Discovered;

    public override void _Ready()
    {
        _tracker.Replied += OnTrackerReplied;
        _tracker.TimedOut += OnTrackerTimedOut;
        _tracker.Discovered += OnTrackerDiscovered;
        PacketPeerUdp socket = new PacketPeerUdp();
        Error error = socket.Bind(0);
        if (error != Error.Ok)
        {
            _log.Error("no query socket: {Error}", error);
            socket.Dispose();
            return;
        }
        socket.SetBroadcastEnabled(true);
        _socket = socket;
    }

    public override void _ExitTree()
    {
        _tracker.Replied -= OnTrackerReplied;
        _tracker.TimedOut -= OnTrackerTimedOut;
        _tracker.Discovered -= OnTrackerDiscovered;
        _socket?.Close();
        _socket = null;
    }

    public void Probe(IEnumerable<ServerEndpoint> endpoints)
    {
        foreach (ServerEndpoint endpoint in endpoints)
        {
            Probe(endpoint);
        }
    }

    public void Probe(ServerEndpoint endpoint)
    {
        if (_socket == null)
            return;
        ulong now = Time.GetTicksMsec();
        uint nonce = _tracker.Track(endpoint, now);
        if (IPAddress.TryParse(endpoint.Address, out _))
            Send(nonce, endpoint.Address, endpoint.QueryPort, now);
        else
            _resolves[nonce] = new PendingResolve(
                IP.ResolveHostnameQueueItem(endpoint.Address), endpoint);
    }

    /// <summary>Drops everything in flight without reporting it.</summary>
    public void Cancel()
    {
        foreach (PendingResolve resolve in _resolves.Values)
        {
            IP.EraseResolveItem(resolve.ResolveId);
        }
        _resolves.Clear();
        _tracker.Cancel();
    }

    /// <summary>Shouts at the subnet on the usual query port. A server on a
    /// custom one is only reachable by direct connect.</summary>
    public void DiscoverLan()
    {
        if (_socket is not PacketPeerUdp socket)
            return;
        int port = ServerQueryProtocol.QueryPort(NetConfig.DEFAULT_PORT);
        uint nonce = _tracker.BeginBroadcast(Time.GetTicksMsec(), port);
        socket.SetDestAddress("255.255.255.255", port);
        socket.PutPacket(ServerQueryProtocol.EncodeRequest(nonce));
    }

    public override void _Process(double delta)
    {
        if (_socket is not PacketPeerUdp socket)
            return;
        ulong now = Time.GetTicksMsec();
        AdvanceResolves(now);
        for (int i = 0; i < MAX_PACKETS_PER_FRAME && socket.GetAvailablePacketCount() > 0; i++)
        {
            byte[] datagram = socket.GetPacket();
            _tracker.OnResponse(datagram, socket.GetPacketIP(), socket.GetPacketPort(), now);
        }
        _tracker.Expire(now);
    }

    private void AdvanceResolves(ulong now)
    {
        foreach ((uint nonce, PendingResolve resolve) in _resolves.ToArray())
        {
            IP.ResolverStatus status = IP.GetResolveItemStatus(resolve.ResolveId);
            if (status == IP.ResolverStatus.Waiting)
                continue;
            string address = status == IP.ResolverStatus.Done
                ? IP.GetResolveItemAddress(resolve.ResolveId)
                : "";
            IP.EraseResolveItem(resolve.ResolveId);
            _resolves.Remove(nonce);
            if (address.Length == 0)
                _tracker.Fail(nonce);
            else
                Send(nonce, address, resolve.Endpoint.QueryPort, now);
        }
    }

    private void Send(uint nonce, string address, int port, ulong now)
    {
        if (_socket is not PacketPeerUdp socket)
            return;
        socket.SetDestAddress(address, port);
        socket.PutPacket(ServerQueryProtocol.EncodeRequest(nonce));
        _tracker.MarkSent(nonce, address, now);
    }

    private void OnTrackerReplied(ServerProbeReply reply) => Replied?.Invoke(reply);

    private void OnTrackerTimedOut(uint nonce, ServerEndpoint endpoint)
    {
        if (_resolves.Remove(nonce, out PendingResolve resolve))
            IP.EraseResolveItem(resolve.ResolveId);
        TimedOut?.Invoke(endpoint);
    }

    private void OnTrackerDiscovered(ServerProbeReply reply) => Discovered?.Invoke(reply);
}
