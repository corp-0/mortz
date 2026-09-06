using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Protocol.Net.Stats;
using Mortz.Runtime.Tests.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class PingsTests : RuntimeServiceTest
{
    private readonly ClientPlayers _players;
    private readonly Pings _pings;

    public PingsTests()
    {
        _players = RegisterRuntime(new ClientPlayers());
        Pings pings = new(_players);
        _pings = RegisterRuntime(pings);
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
    public void ClosedScopeIgnoresTraffic()
    {
        Dispose();

        new PingUpdateMsg([new PeerPing(7, 42)]).Broadcast(Router);

        Assert.Null(_players.Find(7));
    }
}
