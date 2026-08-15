using Mortz.Core.Net;
using Mortz.Core.Net.Stats;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips through the client router.</summary>
public class SessionStatsProtocolTests
{
    private static IReadOnlyList<PeerWins>? Wins(SessionWinsMsg message)
    {
        NetRouter router = new();
        ClientProbe<SessionWinsMsg> probe = new();
        router.Add(probe);
        message.Broadcast(router);
        SessionWinsMsg received = Assert.Single(probe.Messages);
        return SessionStatsProtocol.TryDecode(received, out PeerWins[]? wins) ? wins : null;
    }

    [Fact]
    public void WinsRoundTrip()
    {
        PeerWins[] sent = [new PeerWins(11, 2), new PeerWins(22, 0)];

        Assert.Equal(sent, Wins(SessionStatsProtocol.Encode(sent)));
    }

    [Fact]
    public void TypedRowsRoundTripWithoutParallelArrayAlignment()
    {
        PeerWins[] rows = [new PeerWins(1, 2), new PeerWins(3, 4)];

        Assert.Equal(rows, Wins(new SessionWinsMsg(rows)));
    }
}
