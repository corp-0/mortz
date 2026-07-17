using Mortz.Client.Stats;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Drives the stats tables through the real wire path (loopback
/// NetTransport), so serialization and dispatch are covered too. Swaps the
/// NetTransport.Send static, hence the shared NetTransport collection.</summary>
[Collection("NetTransport")]
public class ClientStatsSessionTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public ClientStatsSessionTests() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));

    public void Dispose() => NetTransport.Send = _original;

    [Fact]
    public void TablesFollowServerUpdates()
    {
        using ClientStatsSession session = new();
        session.Subscribe();
        int changes = 0;
        session.Changed += () => changes++;

        new PingUpdateMsg([7, 8], [42, 108]).Broadcast();
        new SessionWinsMsg([7, 8], [3, 0]).Broadcast();

        Assert.Equal(42, session.PingMs(7));
        Assert.Equal(108, session.PingMs(8));
        Assert.Null(session.PingMs(9));
        Assert.Equal(3, session.Wins(7));
        Assert.Equal(0, session.Wins(8));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void EachUpdateReplacesTheWholeTable()
    {
        using ClientStatsSession session = new();
        session.Subscribe();

        new PingUpdateMsg([7, 8], [42, 108]).Broadcast();
        new PingUpdateMsg([8], [90]).Broadcast();

        Assert.Null(session.PingMs(7));
        Assert.Equal(90, session.PingMs(8));
    }

    [Fact]
    public void ClearForgetsEverythingAndNotifies()
    {
        using ClientStatsSession session = new();
        session.Subscribe();
        new PingUpdateMsg([7], [42]).Broadcast();
        new SessionWinsMsg([7], [2]).Broadcast();
        int changes = 0;
        session.Changed += () => changes++;

        session.Clear();

        Assert.Null(session.PingMs(7));
        Assert.Equal(0, session.Wins(7));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void UnsubscribedSessionIgnoresTraffic()
    {
        using ClientStatsSession subscribed = new();
        subscribed.Subscribe();
        using ClientStatsSession unsubscribed = new();

        new PingUpdateMsg([7], [42]).Broadcast();

        Assert.Equal(42, subscribed.PingMs(7));
        Assert.Null(unsubscribed.PingMs(7));
    }
}
