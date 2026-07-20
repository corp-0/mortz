using Godot;
using Mortz.Client.Stats;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class ClientStatsTests : NodeServiceTest
{
    [Fact]
    public void TablesFollowServerUpdates()
    {
        ClientStats stats = Host(new ClientStats());
        int changes = 0;
        stats.Changed += () => changes++;

        new PingUpdateMsg([7, 8], [42, 108]).Broadcast();
        new SessionWinsMsg([7, 8], [3, 0]).Broadcast();

        Assert.Equal(42, stats.PingMs(7));
        Assert.Equal(108, stats.PingMs(8));
        Assert.Null(stats.PingMs(9));
        Assert.Equal(3, stats.Wins(7));
        Assert.Equal(0, stats.Wins(8));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void EachUpdateReplacesTheWholeTable()
    {
        ClientStats stats = Host(new ClientStats());

        new PingUpdateMsg([7, 8], [42, 108]).Broadcast();
        new PingUpdateMsg([8], [90]).Broadcast();

        Assert.Null(stats.PingMs(7));
        Assert.Equal(90, stats.PingMs(8));
    }

    [Fact]
    public void NodeOutsideTheTreeIgnoresTraffic()
    {
        ClientStats stats = Host(new ClientStats());
        stats.GetParent<Node>().RemoveChild(stats);

        new PingUpdateMsg([7], [42]).Broadcast();

        Assert.Null(stats.PingMs(7));
    }
}
