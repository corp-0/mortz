using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class ScoringStepTests
{
    [Fact]
    public void ProducesOrderedEliminationsAndCurrentStanding()
    {
        Fixture fixture = new(target: 3);
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);
        MatchTick tick = fixture.NewTick([
            fixture.Death(victimId: 2, killerId: 1),
            fixture.Death(victimId: 3, killerId: 1),
        ]);

        fixture.Scoring.Advance(tick);

        Assert.Equal([2, 3], tick.Eliminations.Select(kill => kill.Victim.PeerId));
        Assert.True(tick.Eliminations[0].FirstBlood);
        Assert.False(tick.Eliminations[1].FirstBlood);
        Assert.Equal(new MatchStanding(new Victor.Player(1), 1), tick.Standing);
        Assert.Null(tick.WinningScore);
    }

    [Fact]
    public void WinningDeathStopsLaterDeathsAndRecordsTheDecision()
    {
        Fixture fixture = new(target: 1);
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);
        Death winningDeath = fixture.Death(victimId: 2, killerId: 1);
        MatchTick tick = fixture.NewTick([
            winningDeath,
            fixture.Death(victimId: 3, killerId: 2),
        ]);

        fixture.Scoring.Advance(tick);

        ScoredKill elimination = Assert.Single(tick.Eliminations);
        Assert.Equal(2, elimination.Victim.PeerId);
        Assert.Equal(new WinningScore(winningDeath, elimination), tick.WinningScore);
        Assert.Equal(0, fixture.Scoring.ScoreOf(fixture.Player(3)).Deaths);
    }

    [Fact]
    public void RequiresSimulationToRunFirst()
    {
        Fixture fixture = new();

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Scoring.Advance(new MatchTick(fixture.Context, default)));
    }

    [Fact]
    public void UncreditedDeathDoesNotConsumeFirstBlood()
    {
        Fixture fixture = new();
        fixture.Seat(1);
        fixture.Seat(2);
        MatchTick tick = fixture.NewTick([
            fixture.Death(victimId: 2, killerId: 99),
            fixture.Death(victimId: 2, killerId: 1),
        ]);

        fixture.Scoring.Advance(tick);

        Assert.False(tick.Eliminations[0].FirstBlood);
        Assert.True(tick.Eliminations[1].FirstBlood);
    }

    [Fact]
    public void TeamKillDoesNotConsumeFirstBlood()
    {
        Fixture fixture = new(teams: true);
        fixture.Seat(1, Team.BLUE);
        fixture.Seat(2, Team.RED);
        fixture.Seat(3, Team.BLUE);
        MatchTick tick = fixture.NewTick([
            fixture.Death(victimId: 3, killerId: 1),
            fixture.Death(victimId: 2, killerId: 1),
        ]);

        fixture.Scoring.Advance(tick);

        Assert.Equal(DeathKind.TEAM_KILL, tick.Eliminations[0].Score.Kind);
        Assert.False(tick.Eliminations[0].FirstBlood);
        Assert.True(tick.Eliminations[1].FirstBlood);
    }

    [Fact]
    public void RewardedSuicideGoesToTheNearestLivingEnemy()
    {
        Fixture fixture = new(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);
        fixture.World.Teleport(1, new Vec2(10, 32));
        fixture.World.Teleport(2, new Vec2(40, 32));
        fixture.World.Teleport(3, new Vec2(120, 32));
        MatchTick tick = fixture.NewTick([fixture.Death(victimId: 1, killerId: 1)]);

        fixture.Scoring.Advance(tick);

        Assert.Equal(new KillReward(2, 1), Assert.Single(tick.Eliminations).Score.Reward);
        Assert.Equal(1, fixture.Scoring.ScoreOf(fixture.Player(2)).Kills);
    }

    [Fact]
    public void RewardedSuicideNeverPaysATeammate()
    {
        Fixture fixture = new(
            teams: true,
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        fixture.Seat(1, Team.BLUE);
        fixture.Seat(2, Team.BLUE);
        fixture.Seat(3, Team.RED);
        fixture.World.Teleport(1, new Vec2(10, 32));
        fixture.World.Teleport(2, new Vec2(40, 32));
        fixture.World.Teleport(3, new Vec2(120, 32));
        MatchTick tick = fixture.NewTick([fixture.Death(victimId: 1, killerId: 0)]);

        fixture.Scoring.Advance(tick);

        Assert.Equal(new KillReward(3, 1), Assert.Single(tick.Eliminations).Score.Reward);
        Assert.Equal(0, fixture.Scoring.ScoreOf(fixture.Player(2)).Kills);
    }

    private sealed class Fixture
    {
        private readonly MatchCells _cells = new();

        public Fixture(
            int target = 20,
            bool teams = false,
            SuicidePenalty suicidePenalty = SuicidePenalty.NONE)
        {
            TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
            World = new SimWorld(terrain, new MatchConfig
            {
                Rules = new ModeRules
                {
                    Teams = teams,
                    Victory = new KillsVictoryRules { Target = target },
                    SuicidePenalty = suicidePenalty,
                    SpawnImmunity = 0,
                },
            }, Array.Empty<SpawnPoint>());
            Context = new MatchContext(World, _cells.Seated);
            Scoring = new ScoringStep(World.Config.Rules, _cells.Keys, _cells.Seated);
        }

        public SimWorld World { get; }

        public MatchContext Context { get; }

        public ScoringStep Scoring { get; }

        public Player Player(int peerId) => _cells.GetOrJoin(peerId);

        public void Seat(int peerId, Team? team = null)
        {
            Player player = Player(peerId);
            World.AddPlayer(peerId, team, player.Skin);
            Scoring.Seat(player, team);
        }

        public Death Death(int victimId, int killerId) =>
            new(victimId, World.Players[victimId].Position, killerId, false, ShellId: -1);

        public MatchTick NewTick(IReadOnlyList<Death> deaths)
        {
            MatchTick tick = new(Context, default);
            tick.SetSimulationOutputs([], [], [], deaths);
            return tick;
        }
    }
}
