using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Xunit;

namespace Mortz.Tests.Server;

public class MatchSessionTests
{
    private static MatchSession Session(bool teams = false, int killTarget = 20,
        int victoryLapTicks = 10, IReadOnlyList<Vec2>? spawnPoints = null,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchSession(terrain, new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = teams,
                KillTarget = killTarget,
                SuicidePenalty = suicidePenalty,
            },
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

        ScoredElimination uncredited = match.ScoreDeath(new Death(2, default, 99, false, ShellId: -1))!.Value;
        ScoredElimination credited = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;

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

        ScoredElimination first = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;
        ScoredElimination second = match.ScoreDeath(new Death(2, default, 3, false, ShellId: -1))!.Value;

        Assert.True(first.FirstBlood);
        Assert.False(second.FirstBlood);
    }

    [Fact]
    public void TeamKillDoesNotConsumeFirstBlood()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(1); // blue
        match.AddPlayer(2); // red
        match.AddPlayer(3); // blue

        ScoredElimination teamKill = match.ScoreDeath(new Death(3, default, 1, false, ShellId: -1))!.Value;
        ScoredElimination credited = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;

        Assert.Equal(Scoreboard.DeathKind.TEAM_KILL, teamKill.Score.Kind);
        Assert.False(teamKill.FirstBlood);
        Assert.True(credited.FirstBlood);
    }

    [Fact]
    public void LobbyTeamsCarryIntoTheMatchAndLateJoinersBalance()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(1, lobbyTeam: Team.RED);
        match.AddPlayer(2, lobbyTeam: Team.RED);
        match.AddPlayer(3, lobbyTeam: Team.BLUE);

        Team? lateJoiner = match.AddPlayer(4); // no lobby team: smallest wins

        Assert.Equal(Team.RED, match.World.Players[1].Team);
        Assert.Equal(Team.RED, match.World.Players[2].Team);
        Assert.Equal(Team.BLUE, match.World.Players[3].Team);
        Assert.Equal(Team.BLUE, lateJoiner);
    }

    [Fact]
    public void LobbyTeamsAreIgnoredWhenTeamsAreOff()
    {
        MatchSession match = Session(teams: false);

        Assert.Null(match.AddPlayer(1, lobbyTeam: Team.RED));
    }

    [Fact]
    public void SoloWinnerIsTheOnlyCreditedPeer()
    {
        MatchSession match = Session();
        match.AddPlayer(1);
        match.AddPlayer(2);

        Assert.Equal([1], match.WinnerPeers(new PlayerVictor(1)));
    }

    [Fact]
    public void TeamWinCreditsEveryTeammateStillInTheMatch()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(1); // blue
        match.AddPlayer(2); // red
        match.AddPlayer(3); // blue
        match.AddPlayer(4); // red
        match.RemovePlayer(3);

        int[] winners = match.WinnerPeers(new TeamVictor(Team.BLUE));

        Assert.Equal([1], winners);
        Assert.Equal([2, 4], match.WinnerPeers(new TeamVictor(Team.RED)).Order());
    }

    [Fact]
    public void MatchPointEntersAtOneRemaining_LeavesWhenTheLeadDrops_AnnouncesOnce()
    {
        MatchSession match = Session(killTarget: 2, suicidePenalty: SuicidePenalty.KILL);
        match.AddPlayer(1);
        match.AddPlayer(2);

        match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1));
        MatchFrame enter = match.Step();
        MatchFrame steady = match.Step();
        match.ScoreDeath(new Death(1, default, 1, false, ShellId: -1)); // suicide penalty
        MatchFrame leave = match.Step();

        Assert.Equal(new MatchPointChange(new MatchPoint(1, new PlayerVictor(1))),
            enter.MatchPoint);
        Assert.Null(steady.MatchPoint);
        // The penalty wiped the lead: nobody has scored, so there is no leader.
        Assert.Equal(new MatchPointChange(null), leave.MatchPoint);
    }

    [Fact]
    public void RewardedSuicide_GrantsTheKillToTheNearestLivingEnemy()
    {
        MatchSession match = Session(
            spawnPoints: [new Vec2(10, 64), new Vec2(40, 64), new Vec2(120, 64)],
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        match.AddPlayer(1);
        match.AddPlayer(2);
        match.AddPlayer(3);

        ScoredElimination scored =
            match.ScoreDeath(new Death(1, new Vec2(10, 64), 1, false, ShellId: -1))!.Value;

        Assert.Equal(new Scoreboard.KillReward(2, 1), scored.Score.Reward);
        Assert.Equal(0, match.Scores.Rows[1].Kills);
        Assert.Equal(1, match.Scores.Rows[2].Kills);
    }

    [Fact]
    public void RewardedSuicide_NeverPaysATeammate()
    {
        MatchSession match = Session(teams: true,
            spawnPoints: [new Vec2(10, 64), new Vec2(40, 64), new Vec2(120, 64)],
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        match.AddPlayer(1, Team.BLUE);
        match.AddPlayer(2, Team.BLUE); // closest, but on the victim's team
        match.AddPlayer(3, Team.RED);

        ScoredElimination scored =
            match.ScoreDeath(new Death(1, new Vec2(10, 64), 0, false, ShellId: -1))!.Value;

        Assert.Equal(new Scoreboard.KillReward(3, 1), scored.Score.Reward);
        Assert.Equal(0, match.Scores.Rows[2].Kills);
    }

    [Fact]
    public void VictoryLapFreezesWorldRejectsInputsAndUsesSeparateCountdown()
    {
        MatchSession match = Session(killTarget: 1, victoryLapTicks: 3);
        match.AddPlayer(1);
        match.AddPlayer(2);

        ScoredElimination winner = match.ScoreDeath(
            new Death(2, new Vec2(40, 50), 1, false, ShellId: -1))!.Value;
        Assert.NotNull(winner.Score.Winner);
        Assert.Equal(MatchStage.VICTORY_LAP, match.Stage);

        match.EnqueueInput(1, 0, new PlayerInput(InputButtons.RIGHT));
        Assert.Equal(0, match.World.PendingInputs(1));

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
