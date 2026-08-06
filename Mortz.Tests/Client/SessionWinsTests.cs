using Chickensoft.AutoInject;
using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Core.Net.Stats;
using Mortz.Net;
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

        new SessionWinsMsg([7, 8], [3, 0]).Broadcast();

        Assert.Equal(3, _wins.Of(_players.Find(7)!));
        Assert.Equal(0, _wins.Of(_players.Find(8)!));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void EachBroadcastReplacesTheWholeTable()
    {
        new SessionWinsMsg([7, 8], [3, 1]).Broadcast();
        new SessionWinsMsg([8], [2]).Broadcast();

        Assert.Equal(0, _wins.Of(_players.Find(7)!));
        Assert.Equal(2, _wins.Of(_players.Find(8)!));
    }
}
