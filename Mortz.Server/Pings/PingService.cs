using Mortz.Core.Net.Stats;
using Mortz.Server.Services;

namespace Mortz.Server.Pings;

/// <summary>Republishes the transport's round-trip times once a second.</summary>
public sealed class PingService(IServerLink link) : IAdvance
{
    private const double PING_INTERVAL_SECONDS = 1;

    private double _countdown;

    public void Advance(ServerTime time)
    {
        _countdown -= time.Delta;
        if (_countdown > 0)
            return;
        _countdown = PING_INTERVAL_SECONDS;
        PeerPing[] pings = link.PeerPings();
        if (pings.Length > 0)
            link.Broadcast(new PingUpdateMsg(pings));
    }
}
