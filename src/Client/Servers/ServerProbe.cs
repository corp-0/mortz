using System.Net;
using Godot;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

[GlobalClass]
public partial class ServerProbe : Node, IServerProbe
{
    private class Pending(ServerProbeWork work)
    {
        public ServerProbeWork Work { get; } = work;
        public ServerEndpoint Endpoint => Work.Endpoint;
        public int ResolveId { get; set; } = -1;
        public PacketPeerUdp? Socket { get; set; }
        public A2SProbe? Exchange { get; set; }
    }

    private readonly ServerProbeCoordinator _coordinator = new();
    private readonly List<Pending> _active = [];
    private PacketPeerUdp? _lan;

    public event Action<ServerProbeReply>? Replied;
    public event Action<ServerEndpoint>? TimedOut;
    public event Action<ServerProbeReply>? Discovered;

    public ServerProbe()
    {
        _coordinator.Replied += reply => Replied?.Invoke(reply);
        _coordinator.Discovered += reply => Discovered?.Invoke(reply);
        _coordinator.TimedOut += endpoint => TimedOut?.Invoke(endpoint);
    }

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;
    public override void _ExitTree() => Cancel();

    public void Probe(IEnumerable<ServerEndpoint> endpoints)
    {
        foreach (ServerEndpoint endpoint in endpoints)
        {
            Probe(endpoint);
        }
    }

    public void Probe(ServerEndpoint endpoint) => _coordinator.Schedule(endpoint);

    public void Cancel()
    {
        _coordinator.Cancel();
        foreach (Pending pending in _active)
        {
            Release(pending);
        }
        _active.Clear();
        CloseLan();
    }

    public void DiscoverLan()
    {
        CloseLan();
        PacketPeerUdp socket = new();
        if (socket.Bind(0) != Error.Ok)
        {
            socket.Dispose();
            return;
        }
        socket.SetBroadcastEnabled(true);
        socket.SetDestAddress("255.255.255.255", ServerQueryProtocol.QueryPort(NetConfig.DEFAULT_PORT));
        socket.PutPacket(ServerQueryProtocol.EncodeInfoRequest());
        _lan = socket;
        _coordinator.BeginLan(Time.GetTicksMsec());
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        PollLan(now);
        while (_coordinator.TryStartNext(now, out ServerProbeWork work))
        {
            Pending pending = new(work);
            _active.Add(pending);
            if (IPAddress.TryParse(work.Endpoint.Address, out IPAddress? address))
            {
                Send(pending, address.ToString());
            }
            else
            {
                pending.ResolveId = IP.ResolveHostnameQueueItem(work.Endpoint.Address);
            }
        }
        foreach (Pending pending in _active.ToArray())
        {
            if (!_coordinator.IsActive(pending.Work))
            {
                continue;
            }
            if (_coordinator.HasExpired(pending.Work, now))
            {
                Complete(pending);
                continue;
            }
            if (pending.ResolveId != -1)
            {
                IP.ResolverStatus status = IP.GetResolveItemStatus(pending.ResolveId);
                if (status == IP.ResolverStatus.Waiting)
                {
                    continue;
                }
                string address = status == IP.ResolverStatus.Done ? IP.GetResolveItemAddress(pending.ResolveId) : "";
                IP.EraseResolveItem(pending.ResolveId);
                pending.ResolveId = -1;
                if (address.Length == 0)
                {
                    Complete(pending);
                    continue;
                }
                Send(pending, address);
            }
            if (pending.Socket is not PacketPeerUdp socket || pending.Exchange is not A2SProbe exchange)
            {
                Complete(pending);
                continue;
            }
            for (int i = 0; i < 64 && socket.GetAvailablePacketCount() > 0 && !exchange.IsComplete; i++)
            {
                byte[] response = socket.GetPacket();
                ulong receivedAt = Time.GetTicksMsec();
                byte[]? request = exchange.Receive(response, socket.GetPacketIP(), socket.GetPacketPort(), receivedAt);
                if (request != null)
                {
                    socket.PutPacket(request);
                }
            }
            if (exchange.IsComplete)
            {
                Complete(pending);
            }
        }
    }

    private static void Send(Pending pending, string address)
    {
        PacketPeerUdp socket = new();
        if (socket.ConnectToHost(address, pending.Endpoint.QueryPort) != Error.Ok)
        {
            socket.Dispose();
            return;
        }
        pending.Socket = socket;
        ulong sentAt = Time.GetTicksMsec();
        pending.Exchange = new A2SProbe(pending.Endpoint, address, sentAt, pending.Work.Discovered);
        socket.PutPacket(pending.Exchange.StartRequest());
    }

    private void Complete(Pending pending)
    {
        pending.Exchange?.Finish();
        ServerProbeReply? result = pending.Exchange?.Result;
        Release(pending);
        _active.Remove(pending);
        _coordinator.Complete(pending.Work, result);
    }

    private void PollLan(ulong now)
    {
        if (!_coordinator.IsLanActive(now))
        {
            CloseLan();
            return;
        }
        if (_lan is not PacketPeerUdp socket)
        {
            return;
        }
        for (int i = 0; i < 64 && socket.GetAvailablePacketCount() > 0; i++)
        {
            byte[] packet = socket.GetPacket();
            _coordinator.ObserveLan(packet, socket.GetPacketIP(), socket.GetPacketPort(), now);
        }
    }

    private static void Release(Pending pending)
    {
        if (pending.ResolveId != -1)
        {
            IP.EraseResolveItem(pending.ResolveId);
            pending.ResolveId = -1;
        }
        pending.Socket?.Close();
        pending.Socket?.Dispose();
        pending.Socket = null;
    }

    private void CloseLan()
    {
        _lan?.Close();
        _lan?.Dispose();
        _lan = null;
    }
}
