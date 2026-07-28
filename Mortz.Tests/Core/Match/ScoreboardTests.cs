using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class ScoreboardTests
{
    private static MatchConfig Cfg(bool teams = false,
        WinCondition win = WinCondition.PLAYER_KILLS, int target = 3,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE) => new()
        {
            Teams = teams,
            WinCondition = win,
            KillTarget = target,
            SuicidePenalty = suicidePenalty,
        };

    [Fact]
    public void Kill_CreditsTheKiller_AndCountsTheVictimsDeath()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1))?.Winner);

        Assert.Equal(1, s.Rows[1].Kills);
        Assert.Equal(0, s.Rows[1].Deaths);
        Assert.Equal(0, s.Rows[2].Kills);
        Assert.Equal(1, s.Rows[2].Deaths);
    }

    [Fact]
    public void Suicide_OwnShellOrDeathPit_CountsADeathAndNoKill()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 1)); // own shell
        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 0)); // death pit

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(2, s.Rows[1].Deaths);
    }

    [Fact]
    public void SuicidePenalty_SubtractsAKill_ScoresGoNegative()
    {
        Scoreboard s = new Scoreboard(Cfg(suicidePenalty: SuicidePenalty.KILL));
        s.AddPlayer(1, 0);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 1));
        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 0));

        Assert.Equal(-2, s.Rows[1].Kills);
    }

    [Fact]
    public void SuicidePenaltyNoNegative_StopsAtZero()
    {
        Scoreboard s = new Scoreboard(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL_NO_NEGATIVE));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 2);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1)); // 1-0
        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 1)); // the point goes back
        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 0)); // at zero: nothing to take

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(0, s.TeamKills(1));
    }

    [Fact]
    public void RewardClosestEnemy_GrantsTheKill_AndReportsIt()
    {
        Scoreboard s = new Scoreboard(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.DeathResult result = s.ScoreDeath(
            new Scoreboard.Death(VictimId: 1, KillerId: 1, NearestEnemyId: 2))!.Value;

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(1, s.Rows[2].Kills);
        Assert.Equal(new Scoreboard.KillReward(2, 1), result.Reward);
    }

    [Fact]
    public void RewardClosestEnemy_CanDecideTheMatch()
    {
        Scoreboard s = new Scoreboard(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY, target: 1));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.MatchWinner? winner = s.ScoreDeath(
            new Scoreboard.Death(VictimId: 1, KillerId: 0, NearestEnemyId: 2))?.Winner;

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 2), winner);
    }

    [Fact]
    public void RewardClosestEnemy_NobodyEligible_NothingChanges()
    {
        Scoreboard s = new Scoreboard(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        s.AddPlayer(1, 0);

        Scoreboard.DeathResult result = s.ScoreDeath(
            new Scoreboard.Death(VictimId: 1, KillerId: 1))!.Value;

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Null(result.Reward);
    }

    [Fact]
    public void Teamkill_AwardsNothing_VictimsDeathStillCounts()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1));

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(1, s.Rows[2].Deaths);
        Assert.Equal(0, s.TeamKills(1));
    }

    [Fact]
    public void TeamTotals_AccumulateAtKillTime_AndSurviveLeavers()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.TEAM_KILLS, target: 10));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);
        s.AddPlayer(3, 2);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 1));
        s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 2));
        Assert.Equal(2, s.TeamKills(1));

        s.RemovePlayer(1); // rage quit keeps the team's points on the board
        Assert.Equal(2, s.TeamKills(1));
        Assert.False(s.Rows.ContainsKey(1));
    }

    [Fact]
    public void SuicidePenalty_SubtractsFromTheTeamTotalToo()
    {
        Scoreboard s = new Scoreboard(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 2);

        s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1)); // 1-0
        s.ScoreDeath(new Scoreboard.Death(VictimId: 1, KillerId: 1)); // the point goes back

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(0, s.TeamKills(1));
    }

    [Fact]
    public void PlayerKills_FirstToTargetWins()
    {
        Scoreboard s = new Scoreboard(Cfg(target: 2));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1))?.Winner);
        Scoreboard.MatchWinner? winner =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1))?.Winner;

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 1), winner);
    }

    [Fact]
    public void TeamKills_TeammatesCombineToTheTarget()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.TEAM_KILLS, target: 2));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);
        s.AddPlayer(3, 2);

        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 1))?.Winner);
        Scoreboard.MatchWinner? winner =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 2))?.Winner;

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: true, Id: 1), winner);
    }

    [Fact]
    public void TeamKillsWithoutTeams_PlaysAsPlayerKills()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: false, win: WinCondition.TEAM_KILLS, target: 1));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.MatchWinner? winner =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1))?.Winner;

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 1), winner);
    }

    [Fact]
    public void PlayerKillsWithTeams_AnIndividualCrossingWins()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.PLAYER_KILLS, target: 2));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);
        s.AddPlayer(3, 2);

        // Team 1 has 2 kills split between its players: nobody won yet.
        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 1))?.Winner);
        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 2))?.Winner);

        Scoreboard.MatchWinner? winner =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 3, KillerId: 1))?.Winner;
        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 1), winner);
    }

    [Fact]
    public void KillerWhoAlreadyLeft_CreditsNobody()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);
        s.RemovePlayer(1);

        // shell outlived its shooter
        s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1));

        Assert.Equal(1, s.Rows[2].Deaths);
        Assert.False(s.Rows.ContainsKey(1));
    }

    [Fact]
    public void UnknownVictim_IsIgnored()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);

        Assert.Null(s.ScoreDeath(new Scoreboard.Death(VictimId: 99, KillerId: 1)));
        Assert.Equal(0, s.Rows[1].Kills);
    }

    [Theory]
    [InlineData(0, Scoreboard.DeathKind.FALL)]
    [InlineData(2, Scoreboard.DeathKind.SUICIDE)]
    [InlineData(99, Scoreboard.DeathKind.UNCREDITED)]
    public void ScoreDeath_ClassifiesUncreditedDeaths(int killerId, Scoreboard.DeathKind expected)
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.DeathResult result =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 2, killerId))!.Value;

        Assert.Equal(expected, result.Kind);
        Assert.False(result.CreditedKill);
    }

    [Fact]
    public void ScoreDeath_ReturnsFinalRowsTalliesAndWinner()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.TEAM_KILLS, target: 1));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 2);

        Scoreboard.DeathResult result =
            s.ScoreDeath(new Scoreboard.Death(VictimId: 2, KillerId: 1))!.Value;

        Assert.Equal(Scoreboard.DeathKind.KILL, result.Kind);
        Assert.True(result.CreditedKill);
        Assert.Equal(1, result.Killer!.Value.Kills);
        Assert.Equal(1, result.Victim.Deaths);
        Assert.Equal(1, result.Team1Kills);
        Assert.Equal(new Scoreboard.MatchWinner(true, 1), result.Winner);
    }
}
