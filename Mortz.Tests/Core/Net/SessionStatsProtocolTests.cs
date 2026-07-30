using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class SessionStatsProtocolTests : IDisposable
{
    private const long PEER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static void UseLoopback() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, PEER, payload, isServer: false));

    private static IReadOnlyList<PeerPing>? Pings(Action send)
    {
        UseLoopback();
        IReadOnlyList<PeerPing>? received = null;
        Action<IReadOnlyList<PeerPing>> handler = decoded => received = decoded;
        SessionStatsProtocol.PingsReceived += handler;
        try
        {
            send();
        }
        finally
        {
            SessionStatsProtocol.PingsReceived -= handler;
        }
        return received;
    }

    private static IReadOnlyList<PeerWins>? Wins(Action send)
    {
        UseLoopback();
        IReadOnlyList<PeerWins>? received = null;
        Action<IReadOnlyList<PeerWins>> handler = decoded => received = decoded;
        SessionStatsProtocol.WinsReceived += handler;
        try
        {
            send();
        }
        finally
        {
            SessionStatsProtocol.WinsReceived -= handler;
        }
        return received;
    }

    [Fact]
    public void PingsRoundTrip()
    {
        PeerPing[] sent = [new PeerPing(11, 30), new PeerPing(22, 120)];

        Assert.Equal(sent, Pings(() => SessionStatsProtocol.BroadcastPings(sent)));
    }

    [Fact]
    public void WinsRoundTripOnBothSendPaths()
    {
        PeerWins[] sent = [new PeerWins(11, 2), new PeerWins(22, 0)];

        Assert.Equal(sent, Wins(() => SessionStatsProtocol.BroadcastWins(sent)));
        Assert.Equal(sent, Wins(() => SessionStatsProtocol.SendWinsTo(PEER, sent)));
    }

    [Fact]
    public void AMismatchedTableIsDropped()
    {
        Assert.Null(Pings(() => new PingUpdateMsg([1, 2], [30]).Broadcast()));
        Assert.Null(Wins(() => new SessionWinsMsg([1, 2], [1]).Broadcast()));
    }
}
