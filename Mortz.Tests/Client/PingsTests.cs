using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Core.Net.Stats;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class PingsTests : NodeServiceTest
{
    private readonly ClientPlayers _players;
    private readonly Pings _pings;

    public PingsTests()
    {
        _players = HostRouted(new ClientPlayers());
        Pings pings = new();
        pings.FakeDependency(_players);
        _pings = HostRouted(pings);
    }

    [Fact]
    public void SamplesFollowServerUpdates()
    {
        int changes = 0;
        _pings.Changed += () => changes++;

        new PingUpdateMsg([new PeerPing(7, 42), new PeerPing(8, 108)]).Broadcast(Router);

        Assert.Equal(42, _pings.Of(_players.Find(7)!));
        Assert.Equal(108, _pings.Of(_players.Find(8)!));
        Assert.Null(_pings.Of(_players.GetOrCreate(9)));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void EachUpdateReplacesTheWholeTable()
    {
        new PingUpdateMsg([new PeerPing(7, 42), new PeerPing(8, 108)]).Broadcast(Router);
        new PingUpdateMsg([new PeerPing(8, 90)]).Broadcast(Router);

        Assert.Null(_pings.Of(_players.Find(7)!));
        Assert.Equal(90, _pings.Of(_players.Find(8)!));
    }

    [Fact]
    public void NodeOutsideTheTreeIgnoresTraffic()
    {
        _pings.GetParent<Node>().RemoveChild(_pings);

        new PingUpdateMsg([new PeerPing(7, 42)]).Broadcast(Router);

        Assert.Null(_pings.Of(_players.GetOrCreate(7)));
    }
}
