using Chickensoft.AutoInject;
using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Core.Net.Stats;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class SessionWinsTests : NodeServiceTest
{
    private readonly ClientPlayers _players;
    private readonly SessionWins _wins;

    public SessionWinsTests()
    {
        _players = HostRouted(new ClientPlayers());
        SessionWins wins = new();
        wins.FakeDependency(_players);
        _wins = HostRouted(wins);
    }

    [Fact]
    public void CountsFollowServerBroadcasts()
    {
        int changes = 0;
        _wins.Changed += () => changes++;

        new SessionWinsMsg([new PeerWins(7, 3), new PeerWins(8, 0)]).Broadcast(Router);

        Assert.Equal(3, _wins.Of(_players.Find(7)!));
        Assert.Equal(0, _wins.Of(_players.Find(8)!));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void EachBroadcastReplacesTheWholeTable()
    {
        new SessionWinsMsg([new PeerWins(7, 3), new PeerWins(8, 1)]).Broadcast(Router);
        new SessionWinsMsg([new PeerWins(8, 2)]).Broadcast(Router);

        Assert.Equal(0, _wins.Of(_players.Find(7)!));
        Assert.Equal(2, _wins.Of(_players.Find(8)!));
    }
}
