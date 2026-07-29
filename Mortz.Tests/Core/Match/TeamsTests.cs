using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class TeamsTests
{
    [Fact]
    public void UnassignedPlayersShareNoSide()
    {
        Assert.False(Teams.SameSide(null, null));
        Assert.False(Teams.SameSide(null, Team.BLUE));
        Assert.False(Teams.SameSide(Team.BLUE, null));
    }

    [Fact]
    public void SameSideIsExactlyTheSameAssignment()
    {
        Assert.True(Teams.SameSide(Team.BLUE, Team.BLUE));
        Assert.True(Teams.SameSide(Team.RED, Team.RED));
        Assert.False(Teams.SameSide(Team.BLUE, Team.RED));
    }

    [Fact]
    public void SmallestTakesTheThinnestTeam()
    {
        Assert.Equal(Team.RED, Teams.Smallest([Team.BLUE, Team.BLUE, Team.RED]));
        Assert.Equal(Team.BLUE, Teams.Smallest([Team.RED]));
    }

    [Fact]
    public void SmallestBreaksTiesTowardsTheFirstTeam()
    {
        Assert.Equal(Team.BLUE, Teams.Smallest([]));
        Assert.Equal(Team.BLUE, Teams.Smallest([Team.BLUE, Team.RED]));
    }

    [Fact]
    public void UnassignedSeatsCountForNobody()
    {
        Assert.Equal(Team.RED, Teams.Smallest([Team.BLUE, null, null]));
    }

    [Fact]
    public void DealAlternatesAcrossTheWholeRoster()
    {
        Assert.Equal([Team.BLUE, Team.RED, Team.BLUE, Team.RED],
            Enumerable.Range(0, 4).Select(Teams.Deal));
    }
}
