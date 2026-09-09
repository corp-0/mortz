using System.Net;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

public record ServerProbeWork(long Id, long Generation, ServerEndpoint Endpoint, bool Discovered, ulong StartedAt);

public class ServerProbeCoordinator
{
    public const int MAX_TRANSIENT_RESULTS = 1_000;
    private readonly Queue<(ServerEndpoint Endpoint, bool Discovered)> _waiting = new();
    private readonly Dictionary<long, ServerProbeWork> _active = new();
    private readonly HashSet<ServerEndpoint> _scheduled = [];
    private readonly HashSet<ServerEndpoint> _discoveries = [];
    private long _generation;
    private long _nextId;
    private ulong? _lanDeadline;
    public event Action<ServerProbeReply>? Replied;
    public event Action<ServerProbeReply>? Discovered;
    public event Action<ServerEndpoint>? TimedOut;

    public void Schedule(ServerEndpoint endpoint)
    {
        if (_scheduled.Add(endpoint))
            _waiting.Enqueue((endpoint, false));
    }

    public bool TryStartNext(ulong nowMs, out ServerProbeWork work)
    {
        work = null!;
        if (_active.Count >= A2SProbe.MAX_CONCURRENT || !_waiting.TryDequeue(out (ServerEndpoint Endpoint, bool Discovered) next))
            return false;
        work = new ServerProbeWork(++_nextId, _generation, next.Endpoint, next.Discovered, nowMs);
        _active.Add(work.Id, work);
        return true;
    }

    public bool IsActive(ServerProbeWork work) => work.Generation == _generation && _active.ContainsKey(work.Id);

    public bool HasExpired(ServerProbeWork work, ulong nowMs) => nowMs - work.StartedAt >= A2SProbe.TIMEOUT_MS;

    public void Complete(ServerProbeWork work, ServerProbeReply? result)
    {
        if (work.Generation != _generation || !_active.Remove(work.Id))
            return;
        _scheduled.Remove(work.Endpoint);
        if (result is ServerProbeReply reply)
        {
            if (work.Discovered)
                Discovered?.Invoke(reply);
            else
                Replied?.Invoke(reply);
        }
        else if (!work.Discovered)
            TimedOut?.Invoke(work.Endpoint);
    }

    public void BeginLan(ulong nowMs) => _lanDeadline = nowMs + A2SProbe.TIMEOUT_MS;
    public bool IsLanActive(ulong nowMs) => _lanDeadline.HasValue && nowMs < _lanDeadline.Value;

    public void ObserveLan(byte[] packet, string address, int port, ulong nowMs)
    {
        if (!IsLanActive(nowMs) || port != ServerQueryProtocol.QueryPort(NetConfig.DEFAULT_PORT) ||
            !IPAddress.TryParse(address, out IPAddress? source) ||
            !ServerQueryProtocol.TryDecodeChallenge(packet, out _) &&
            !ServerQueryProtocol.TryDecodeInfo(packet, NetConfig.DEFAULT_PORT, out _))
            return;
        ServerEndpoint endpoint = new(source.ToString(), NetConfig.DEFAULT_PORT, port);
        if (_discoveries.Count >= MAX_TRANSIENT_RESULTS || !_discoveries.Add(endpoint))
            return;
        if (_scheduled.Add(endpoint))
            _waiting.Enqueue((endpoint, true));
    }

    public void Cancel()
    {
        _generation++;
        _waiting.Clear();
        _active.Clear();
        _scheduled.Clear();
        _discoveries.Clear();
        _lanDeadline = null;
    }
}
