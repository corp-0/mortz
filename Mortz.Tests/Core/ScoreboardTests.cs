using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class ScoreboardTests
{
    private static MatchConfig Cfg(bool teams = false,
        WinCondition win = WinCondition.PLAYER_KILLS, int target = 3,
        bool suicidePenalty = false) => new()
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

        Assert.Null(s.RecordDeath(victimId: 2, killerId: 1));

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

        s.RecordDeath(victimId: 1, killerId: 1); // own shell
        s.RecordDeath(victimId: 1, killerId: 0); // death pit

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(2, s.Rows[1].Deaths);
    }

    [Fact]
    public void SuicidePenalty_SubtractsAKill_ScoresGoNegative()
    {
        Scoreboard s = new Scoreboard(Cfg(suicidePenalty: true));
        s.AddPlayer(1, 0);

        s.RecordDeath(victimId: 1, killerId: 1);
        s.RecordDeath(victimId: 1, killerId: 0);

        Assert.Equal(-2, s.Rows[1].Kills);
    }

    [Fact]
    public void Teamkill_AwardsNothing_VictimsDeathStillCounts()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);

        s.RecordDeath(victimId: 2, killerId: 1);

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

        s.RecordDeath(victimId: 3, killerId: 1);
        s.RecordDeath(victimId: 3, killerId: 2);
        Assert.Equal(2, s.TeamKills(1));

        s.RemovePlayer(1); // rage quit keeps the team's points on the board
        Assert.Equal(2, s.TeamKills(1));
        Assert.False(s.Rows.ContainsKey(1));
    }

    [Fact]
    public void SuicidePenalty_SubtractsFromTheTeamTotalToo()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, suicidePenalty: true));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 2);

        s.RecordDeath(victimId: 2, killerId: 1); // 1-0
        s.RecordDeath(victimId: 1, killerId: 1); // the point goes back

        Assert.Equal(0, s.Rows[1].Kills);
        Assert.Equal(0, s.TeamKills(1));
    }

    [Fact]
    public void PlayerKills_FirstToTargetWins()
    {
        Scoreboard s = new Scoreboard(Cfg(target: 2));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Assert.Null(s.RecordDeath(victimId: 2, killerId: 1));
        Scoreboard.MatchWinner? winner = s.RecordDeath(victimId: 2, killerId: 1);

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 1), winner);
    }

    [Fact]
    public void TeamKills_TeammatesCombineToTheTarget()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.TEAM_KILLS, target: 2));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 1);
        s.AddPlayer(3, 2);

        Assert.Null(s.RecordDeath(victimId: 3, killerId: 1));
        Scoreboard.MatchWinner? winner = s.RecordDeath(victimId: 3, killerId: 2);

        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: true, Id: 1), winner);
    }

    [Fact]
    public void TeamKillsWithoutTeams_PlaysAsPlayerKills()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: false, win: WinCondition.TEAM_KILLS, target: 1));
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.MatchWinner? winner = s.RecordDeath(victimId: 2, killerId: 1);

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
        Assert.Null(s.RecordDeath(victimId: 3, killerId: 1));
        Assert.Null(s.RecordDeath(victimId: 3, killerId: 2));

        Scoreboard.MatchWinner? winner = s.RecordDeath(victimId: 3, killerId: 1);
        Assert.Equal(new Scoreboard.MatchWinner(ByTeam: false, Id: 1), winner);
    }

    [Fact]
    public void KillerWhoAlreadyLeft_CreditsNobody()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);
        s.RemovePlayer(1);

        s.RecordDeath(victimId: 2, killerId: 1); // shell outlived its shooter

        Assert.Equal(1, s.Rows[2].Deaths);
        Assert.False(s.Rows.ContainsKey(1));
    }

    [Fact]
    public void UnknownVictim_IsIgnored()
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);

        Assert.Null(s.RecordDeath(victimId: 99, killerId: 1));
        Assert.Equal(0, s.Rows[1].Kills);
    }

    [Theory]
    [InlineData(0, Scoreboard.DeathKind.Fall)]
    [InlineData(2, Scoreboard.DeathKind.Suicide)]
    [InlineData(99, Scoreboard.DeathKind.Uncredited)]
    public void ScoreDeath_ClassifiesUncreditedDeaths(int killerId, Scoreboard.DeathKind expected)
    {
        Scoreboard s = new Scoreboard(Cfg());
        s.AddPlayer(1, 0);
        s.AddPlayer(2, 0);

        Scoreboard.DeathResult result = s.ScoreDeath(victimId: 2, killerId)!.Value;

        Assert.Equal(expected, result.Kind);
        Assert.False(result.CreditedKill);
    }

    [Fact]
    public void ScoreDeath_ReturnsFinalRowsTalliesAndWinner()
    {
        Scoreboard s = new Scoreboard(Cfg(teams: true, win: WinCondition.TEAM_KILLS, target: 1));
        s.AddPlayer(1, 1);
        s.AddPlayer(2, 2);

        Scoreboard.DeathResult result = s.ScoreDeath(victimId: 2, killerId: 1)!.Value;

        Assert.Equal(Scoreboard.DeathKind.Kill, result.Kind);
        Assert.True(result.CreditedKill);
        Assert.Equal(1, result.Killer!.Value.Kills);
        Assert.Equal(1, result.Victim.Deaths);
        Assert.Equal(1, result.Team1Kills);
        Assert.Equal(new Scoreboard.MatchWinner(true, 1), result.Winner);
    }
}
