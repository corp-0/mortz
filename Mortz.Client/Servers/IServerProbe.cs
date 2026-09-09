using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

public interface IServerProbe
{
    event Action<ServerProbeReply>? Replied;
    event Action<ServerEndpoint>? TimedOut;
    event Action<ServerProbeReply>? Discovered;
    void Probe(ServerEndpoint endpoint);
    void Probe(IEnumerable<ServerEndpoint> endpoints);
    void DiscoverLan();
    void Cancel();
}
