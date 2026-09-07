using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match.Modes;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Runtime.Tests.Server.Match;

public class MatchScoresTests
{
    private readonly MatchCells _cells = new();

    private static ModeRules Cfg(bool teams = false, int target = 3,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE,
        bool killLead = false,
        int leadTarget = 3) => new()
        {
            Teams = teams,
            EndCondition = killLead
                ? new ScoreLeadRules { Target = leadTarget }
                : new ScoreTargetRules { Target = target },
            SuicidePenalty = suicidePenalty,
        };

    private GameMode _mode = null!;
    private SimWorld _world = null!;

    private MatchScores Scores(ModeRules config)
    {
        _mode = GameMode.Create(config.ToSnapshot());
        _world = new SimWorld(new TerrainMask(64, 64, (_, _) => false, (_, _) => false),
            new MatchConfig { Rules = config });
        return new MatchScores(config, _cells.Keys, _cells.Seated);
    }

    private Victor? Winner(MatchScores scores) =>
        _mode.Evaluate(new GameModeContext(_world, scores.Rows(), scores.TeamKills))?.Winner;

    private MatchStanding Standing(MatchScores scores) =>
        _mode.Standing(new GameModeContext(_world, scores.Rows(), scores.TeamKills));

    private Player Seat(MatchScores scores, int peerId, Team? team = null)
    {
        Player player = _cells.GetOrJoin(peerId);
        scores.Seat(player, team);
        return player;
    }

    [Fact]
    public void ATeamWithTheTeamsRuleOff_IsUnconstructible()
    {
        MatchScores s = Scores(Cfg());
        Player player = _cells.GetOrJoin(1);

        Assert.Throws<ArgumentException>(() => s.Seat(player, Team.BLUE));
    }

    [Fact]
    public void SameTeamKillsAreTeamkills_ButUnassignedKillersAreNot()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3); // never took a side
        Player p4 = Seat(s, 4);

        Assert.Equal(DeathKind.TEAM_KILL,
            s.ScoreDeath(p1.PeerId, p2, p1, null).Kind);
        Assert.Equal(DeathKind.KILL,
            s.ScoreDeath(p3.PeerId, p4, p3, null).Kind);
        Assert.Equal(DeathKind.KILL,
            s.ScoreDeath(p3.PeerId, p2, p3, null).Kind);
    }

    [Fact]
    public void Kill_CreditsTheKiller_AndCountsTheVictimsDeath()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Assert.Null(Winner(s));

        Assert.Equal(1, s.Of(p1).Kills);
        Assert.Equal(0, s.Of(p1).Deaths);
        Assert.Equal(0, s.Of(p2).Kills);
        Assert.Equal(1, s.Of(p2).Deaths);
    }

    [Fact]
    public void Suicide_OwnShellOrDeathPit_CountsADeathAndNoKill()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);

        s.ScoreDeath(p1.PeerId, p1, p1, null); // own shell
        s.ScoreDeath(0, p1, null, null); // death pit

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(2, s.Of(p1).Deaths);
    }

    [Fact]
    public void SuicidePenalty_SubtractsAKill_ScoresGoNegative()
    {
        MatchScores s = Scores(Cfg(suicidePenalty: SuicidePenalty.KILL));
        Player p1 = Seat(s, 1);

        s.ScoreDeath(p1.PeerId, p1, p1, null);
        s.ScoreDeath(0, p1, null, null);

        Assert.Equal(-2, s.Of(p1).Kills);
    }

    [Fact]
    public void SuicidePenaltyNoNegative_StopsAtZero()
    {
        MatchScores s = Scores(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL_NO_NEGATIVE));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        s.ScoreDeath(p1.PeerId, p2, p1, null); // 1-0
        s.ScoreDeath(p1.PeerId, p1, p1, null); // the point goes back
        s.ScoreDeath(0, p1, null, null); // at zero: nothing to take

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void RewardClosestEnemy_GrantsTheKill_AndReportsIt()
    {
        MatchScores s = Scores(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        DeathScore result = s.ScoreDeath(p1.PeerId, p1, p1, p2);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(1, s.Of(p2).Kills);
        Assert.Equal(new KillReward(2, 1), result.Reward);
    }

    [Fact]
    public void RewardClosestEnemy_CanDecideTheMatch()
    {
        MatchScores s = Scores(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY, target: 1));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        s.ScoreDeath(0, p1, null, p2);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Player(2), winner);
    }

    [Fact]
    public void RewardClosestEnemy_NobodyEligible_NothingChanges()
    {
        MatchScores s = Scores(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        Player p1 = Seat(s, 1);

        DeathScore result = s.ScoreDeath(p1.PeerId, p1, p1, null);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Null(result.Reward);
    }

    [Fact]
    public void Teamkill_AwardsNothing_VictimsDeathStillCounts()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);

        s.ScoreDeath(p1.PeerId, p2, p1, null);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(1, s.Of(p2).Deaths);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void TeamTotals_AccumulateAtKillTime_AndSurviveLeavers()
    {
        MatchScores s = Scores(Cfg(teams: true, target: 10));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3, Team.RED);

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        s.ScoreDeath(p2.PeerId, p3, p2, null);
        Assert.Equal(2, s.TeamKills[Team.BLUE]);

        _cells.Leave(p1); // rage quit keeps the team's points on the board
        Assert.Equal(2, s.TeamKills[Team.BLUE]);
        Assert.DoesNotContain(s.Rows(), row => row.Player.PeerId == 1);
    }

    [Fact]
    public void SuicidePenalty_SubtractsFromTheTeamTotalToo()
    {
        MatchScores s = Scores(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        s.ScoreDeath(p1.PeerId, p2, p1, null); // 1-0
        s.ScoreDeath(p1.PeerId, p1, p1, null); // the point goes back

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void KillsWithoutTeams_FirstPlayerToTargetWins()
    {
        MatchScores s = Scores(Cfg(target: 2));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Assert.Null(Winner(s));
        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Player(1), winner);
    }

    [Fact]
    public void KillsWithTeams_TeammatesCombineToTheTarget()
    {
        MatchScores s = Scores(Cfg(teams: true, target: 2));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3, Team.RED);

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        Assert.Null(Winner(s));
        s.ScoreDeath(p2.PeerId, p3, p2, null);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Team(Team.BLUE), winner);
    }

    [Fact]
    public void KillsStandingWithTeams_UsesTheCombinedTeamTotal()
    {
        MatchScores s = Scores(Cfg(teams: true, target: 3));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3, Team.RED);

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        s.ScoreDeath(p2.PeerId, p3, p2, null);

        Assert.Equal(new MatchStanding(
            Leader: new Victor.Team(Team.BLUE), Remaining: 1), Standing(s));
    }

    [Fact]
    public void KillLeadWithoutTeams_RequiresALeadOverTheRunnerUp()
    {
        MatchScores s = Scores(Cfg(
            killLead: true, leadTarget: 2));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);
        Player p3 = Seat(s, 3);

        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Assert.Null(Winner(s));
        Assert.Equal(new MatchStanding(new Victor.Player(1), 1), Standing(s));

        s.ScoreDeath(p2.PeerId, p3, p2, null);
        Assert.Null(Winner(s));
        Assert.Equal(new MatchStanding(null, 2), Standing(s));

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        Assert.Null(Winner(s));
        Assert.Equal(new MatchStanding(new Victor.Player(1), 1), Standing(s));

        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Player(1), winner);
    }

    [Fact]
    public void KillLeadWithTeams_CombinesTeammateKills()
    {
        MatchScores s = Scores(Cfg(
            teams: true, killLead: true, leadTarget: 2));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3, Team.RED);

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        Assert.Null(Winner(s));
        s.ScoreDeath(p2.PeerId, p3, p2, null);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Team(Team.BLUE), winner);
    }

    [Fact]
    public void KillLeadCanBeDecidedByASuicidePenalty()
    {
        MatchScores s = Scores(Cfg(
            suicidePenalty: SuicidePenalty.KILL,
            killLead: true,
            leadTarget: 2));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        s.ScoreDeath(p1.PeerId, p2, p1, null);
        Assert.Null(Winner(s));
        s.ScoreDeath(p2.PeerId, p2, p2, null);
        Victor? winner = Winner(s);

        Assert.Equal(new Victor.Player(1), winner);
    }

    [Fact]
    public void KillLeadNeedsAtLeastTwoCompetitors()
    {
        MatchScores s = Scores(Cfg(
            killLead: true, leadTarget: 1));
        Player p1 = Seat(s, 1);

        s.ScoreDeath(p1.PeerId, p1, p1, null);
        Assert.Null(Winner(s));
        Assert.Equal(new MatchStanding(null, 1), Standing(s));
    }

    [Fact]
    public void KillerWhoAlreadyLeft_CreditsNobody()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);
        _cells.Leave(p1);

        // shell outlived its shooter
        DeathScore result = s.ScoreDeath(p1.PeerId, p2, null, null);

        Assert.Equal(DeathKind.UNCREDITED, result.Kind);
        Assert.Equal(1, s.Of(p2).Deaths);
        Assert.DoesNotContain(s.Rows(), row => row.Player.PeerId == 1);
    }

    [Theory]
    [InlineData(0, DeathKind.FALL)]
    [InlineData(2, DeathKind.SUICIDE)]
    [InlineData(99, DeathKind.UNCREDITED)]
    public void ScoreDeath_ClassifiesUncreditedDeaths(int killerId, DeathKind expected)
    {
        MatchScores s = Scores(Cfg());
        Seat(s, 1);
        Player p2 = Seat(s, 2);

        Player? killer = killerId == p2.PeerId ? p2 : null;
        DeathScore result = s.ScoreDeath(killerId, p2, killer, null);

        Assert.Equal(expected, result.Kind);
        Assert.False(result.CreditedKill);
    }

    [Fact]
    public void ScoreDeath_ReturnsFinalRowsTalliesAndWinner()
    {
        MatchScores s = Scores(Cfg(teams: true, target: 1));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        DeathScore result = s.ScoreDeath(p1.PeerId, p2, p1, null);

        Assert.Equal(DeathKind.KILL, result.Kind);
        Assert.True(result.CreditedKill);
        Assert.Equal(1, result.Killer!.Value.Kills);
        Assert.Equal(1, result.Victim.Deaths);
        Assert.Equal(1, result.TeamKills[Team.BLUE]);
        Assert.Equal(new Victor.Team(Team.BLUE), Winner(s));
    }
}
