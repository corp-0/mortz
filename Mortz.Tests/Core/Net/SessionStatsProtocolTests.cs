using Mortz.Core.Net;
using Mortz.Core.Net.Stats;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class SessionStatsProtocolTests : IDisposable
{
    private const int PEER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static IReadOnlyList<PeerWins>? Wins(Action send)
    {
        NetRouter router = new();
        ClientProbe<SessionWinsMsg> probe = new();
        router.Add(probe);
        NetTransport.Send = (id, payload, _, _) => Assert.True(router.Dispatch(id, payload));
        send();
        SessionWinsMsg received = Assert.Single(probe.Messages);
        return SessionStatsProtocol.TryDecode(received, out PeerWins[]? wins) ? wins : null;
    }

    [Fact]
    public void WinsRoundTripOnBothSendPaths()
    {
        PeerWins[] sent = [new PeerWins(11, 2), new PeerWins(22, 0)];

        Assert.Equal(sent, Wins(() => SessionStatsProtocol.Encode(sent).Broadcast()));
        Assert.Equal(sent, Wins(() => SessionStatsProtocol.Encode(sent).SendTo(PEER)));
    }

    [Fact]
    public void AMismatchedTableIsDropped()
    {
        Assert.Null(Wins(() => new SessionWinsMsg([1, 2], [1]).Broadcast()));
    }
}
