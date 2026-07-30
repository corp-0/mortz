using Mortz.Core.Match;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class LobbyMemberTests
{
    private static LobbyMember Member(bool ready = false, Team? team = null) =>
        new(1, "Alice", ready, team);

    [Fact]
    public void ALobbyMemberNeedsAPositivePeerId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LobbyMember(0, "Alice", true, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LobbyMember(-1, "Alice", true, null));
    }

    [Fact]
    public void ReadyIsTheOnlyThingWithReadyChanges()
    {
        LobbyMember before = Member(ready: false, team: Team.BLUE);

        LobbyMember after = before.WithReady(true);

        Assert.True(after.Ready);
        Assert.Equal(before.PeerId, after.PeerId);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Team, after.Team);
    }

    [Fact]
    public void TeamIsTheOnlyThingOnTeamChanges()
    {
        LobbyMember before = Member(ready: true, team: Team.BLUE);

        LobbyMember after = before.OnTeam(Team.RED);

        Assert.Equal(Team.RED, after.Team);
        Assert.Equal(before.PeerId, after.PeerId);
        Assert.Equal(before.Name, after.Name);
        Assert.True(after.Ready);
    }

    [Fact]
    public void OnTeamClearsTheTeam()
    {
        Assert.Null(Member(team: Team.RED).OnTeam(null).Team);
    }
}
