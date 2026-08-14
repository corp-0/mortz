using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class EndingStepTests
{
    [Fact]
    public void WinningScoreStartsVictoryLapAndCapturesTheFinalKill()
    {
        Fixture fixture = new();
        Victor.Player winner = new(1);
        Death death = new(2, new Vec2(10, 10), 1, false, ShellId: 7);
        Explosion nearest = new(11, 10, 20, OwnerId: 1, SpawnSeq: 7);
        Explosion farther = new(20, 10, 20, OwnerId: 1, SpawnSeq: 8);
        Explosion otherPlayer = new(10, 10, 20, OwnerId: 3, SpawnSeq: 9);
        ScoredKill elimination = fixture.WinningKill(death, winner);
        MatchTick tick = fixture.Tick(
            [farther, otherPlayer, nearest],
            new WinningScore(death, elimination));

        fixture.Ending.Advance(tick);

        Assert.Equal(MatchStage.VICTORY_LAP, fixture.Context.Stage);
        Assert.Equal(winner, fixture.Ending.Winner);
        Assert.Equal(winner, tick.MatchEnded);
        Assert.Equal(nearest, tick.FinalKill!.Value.Explosion);
        Assert.Equal(tick.FinalKill, fixture.Ending.FinalKill);
        Assert.False(tick.ReturnToLobby);
    }

    [Fact]
    public void VictoryLapUsesItsOwnCountdownWithoutAdvancingTheWorld()
    {
        Fixture fixture = new(victoryLapTicks: 2);
        fixture.Ending.BeginVictoryLap(fixture.Context, new Victor.Player(1));

        MatchTick first = new(fixture.Context, default);
        fixture.Ending.AdvanceVictoryLap(first);
        MatchTick second = new(fixture.Context, default);
        fixture.Ending.AdvanceVictoryLap(second);

        Assert.False(first.ReturnToLobby);
        Assert.True(second.ReturnToLobby);
        Assert.Equal(0, fixture.Context.World.Tick);
    }

    [Fact]
    public void RequiresScoringToRunFirst()
    {
        Fixture fixture = new();
        MatchTick tick = new(fixture.Context, default);

        Assert.Throws<InvalidOperationException>(() => fixture.Ending.Advance(tick));
    }

    private sealed class Fixture
    {
        public Fixture(int victoryLapTicks = 10)
        {
            TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
            SimWorld world = new(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
            Context = new MatchContext(world, new Dictionary<int, Player>());
            Ending = new EndingStep(victoryLapTicks);
        }

        public MatchContext Context { get; }

        public EndingStep Ending { get; }

        public MatchTick Tick(
            IReadOnlyList<Explosion> explosions,
            WinningScore? winningScore)
        {
            MatchTick tick = new(Context, default);
            tick.SetSimulationOutputs([], explosions, [], []);
            tick.SetScoring([], new MatchStanding(null, 1), winningScore);
            return tick;
        }

        public ScoredKill WinningKill(Death death, Victor winner)
        {
            Player victim = new(death.PeerId, $"Player {death.PeerId}", 0, 1);
            DeathScore score = new(
                death.KillerId,
                death.PeerId,
                DeathKind.KILL,
                default,
                default,
                null,
                default,
                winner);
            return new ScoredKill(
                null, victim, score, death.Owned, FirstBlood: false, death.ShellId);
        }
    }
}
