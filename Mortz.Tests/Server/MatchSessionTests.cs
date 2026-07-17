using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Xunit;

namespace Mortz.Tests.Server;

public class MatchSessionTests
{
    private static MatchSession Session(bool teams = false, int killTarget = 20,
        int victoryLapTicks = 10, IReadOnlyList<Vec2>? spawnPoints = null)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchSession(terrain, new MatchConfig
        {
            Teams = teams,
            KillTarget = killTarget,
        }, seed: 1, victoryLapTicks, spawnPoints);
    }

    [Fact]
    public void AuthoredSpawnPoints_ReachTheWorld()
    {
        MatchSession match = Session(spawnPoints: [new Vec2(32, 64), new Vec2(96, 64)]);

        match.AddPlayer(50);
        match.AddPlayer(10);

        Assert.Equal(new Vec2(32, 64), match.World.Players[50].Position);
        Assert.Equal(new Vec2(96, 64), match.World.Players[10].Position);
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

        Assert.Equal(Scoreboard.DeathKind.TEAM_KILL, teamKill.Score.Kind);
        Assert.False(teamKill.FirstBlood);
        Assert.True(credited.FirstBlood);
    }

    [Fact]
    public void SoloWinnerIsTheOnlyCreditedPeer()
    {
        MatchSession match = Session();
        match.AddPlayer(1);
        match.AddPlayer(2);

        Assert.Equal([1], match.WinnerPeers(new Scoreboard.MatchWinner(false, 1)));
    }

    [Fact]
    public void TeamWinCreditsEveryTeammateStillInTheMatch()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(1); // team 1
        match.AddPlayer(2); // team 2
        match.AddPlayer(3); // team 1
        match.AddPlayer(4); // team 2
        match.RemovePlayer(3);

        int[] winners = match.WinnerPeers(new Scoreboard.MatchWinner(true, 1));

        Assert.Equal([1], winners);
        Assert.Equal([2, 4], match.WinnerPeers(new Scoreboard.MatchWinner(true, 2)).Order());
    }

    [Fact]
    public void VictoryLapFreezesWorldRejectsInputsAndUsesSeparateCountdown()
    {
        MatchSession match = Session(killTarget: 1, victoryLapTicks: 3);
        match.AddPlayer(1);
        match.AddPlayer(2);

        ScoredElimination winner = match.ScoreDeath(
            new ServerDeath(2, new Vec2(40, 50), 1, false))!.Value;
        Assert.NotNull(winner.Score.Winner);
        Assert.Equal(MatchStage.VICTORY_LAP, match.Stage);

        match.EnqueueInput(1, 0, new PlayerInput(InputButtons.RIGHT, 0));
        Assert.Equal(0, match.World.PendingInputs(1));
        Assert.Null(match.DebugCarve(20, 20));

        MatchFrame first = match.Step();
        MatchFrame second = match.Step();
        MatchFrame third = match.Step();

        Assert.Equal(0, match.World.Tick);
        Assert.False(first.ReturnToLobby);
        Assert.False(second.ReturnToLobby);
        Assert.True(third.ReturnToLobby);
        Assert.Empty(first.MortarEvents);
        Assert.Empty(first.Explosions);
    }
}
