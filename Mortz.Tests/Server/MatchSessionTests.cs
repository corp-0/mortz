using Mortz.Core;
using Mortz.Server;
using Xunit;

namespace Mortz.Tests.Server;

public class MatchSessionTests
{
    private static MatchSession Session(bool teams = false)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchSession(terrain, new MatchConfig
        {
            Teams = teams,
            KillTarget = 20,
        }, seed: 1, victoryLapTicks: 10);
    }

    [Fact]
    public void UncreditedDeathDoesNotConsumeGlobalFirstBlood()
    {
        MatchSession match = Session();
        match.AddPlayer(1);
        match.AddPlayer(2);

        ScoredElimination uncredited = match.ScoreDeath(new ServerDeath(2, default, 99, false))!.Value;
        ScoredElimination credited = match.ScoreDeath(new ServerDeath(2, default, 1, false))!.Value;

        Assert.False(uncredited.FirstBlood);
        Assert.True(credited.FirstBlood);
    }

    [Fact]
    public void FirstBloodIsClaimedOnceAcrossAllPlayers()
    {
        MatchSession match = Session();
        match.AddPlayer(1);
        match.AddPlayer(2);
        match.AddPlayer(3);

        ScoredElimination first = match.ScoreDeath(new ServerDeath(2, default, 1, false))!.Value;
        ScoredElimination second = match.ScoreDeath(new ServerDeath(2, default, 3, false))!.Value;

        Assert.True(first.FirstBlood);
        Assert.False(second.FirstBlood);
    }

    [Fact]
    public void TeamKillDoesNotConsumeFirstBlood()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(1); // team 1
        match.AddPlayer(2); // team 2
        match.AddPlayer(3); // team 1

        ScoredElimination teamKill = match.ScoreDeath(new ServerDeath(3, default, 1, false))!.Value;
        ScoredElimination credited = match.ScoreDeath(new ServerDeath(2, default, 1, false))!.Value;

        Assert.Equal(Scoreboard.DeathKind.TeamKill, teamKill.Score.Kind);
        Assert.False(teamKill.FirstBlood);
        Assert.True(credited.FirstBlood);
    }
}
