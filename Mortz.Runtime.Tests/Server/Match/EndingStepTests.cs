using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Modes;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class EndingStepTests
{
    [Fact]
    public void ModeOutcomeStartsVictoryLapWithTheExplicitReplay()
    {
        Fixture fixture = new();
        Victor.Player winner = new(1);
        Death death = new(2, new Vec2(10, 10), 1, false, ShellId: 7);
        Explosion nearest = new(11, 10, 20, OwnerId: 1, SpawnSeq: 7, ShellId: 8);
        Explosion farther = new(20, 10, 20, OwnerId: 1, SpawnSeq: 8, ShellId: 7);
        Explosion otherPlayer = new(10, 10, 20, OwnerId: 3, SpawnSeq: 9, ShellId: 9);
        ScoredKill elimination = fixture.WinningKill(death);
        FinalKillEvent replay = FinalKillEvent.Capture(0, elimination, death, [farther, otherPlayer, nearest]);
        EndingOutput tick = fixture.Ending.Apply(fixture.Context, new MatchOutcome(winner, replay));


        Assert.Equal(MatchStage.VICTORY_LAP, fixture.Context.Stage);
        Assert.Equal(winner, fixture.Ending.Winner);
        Assert.Equal(winner, tick.Winner);
        Assert.Equal(farther, tick.FinalKill!.Value.Explosion);
        Assert.Equal(tick.FinalKill, fixture.Ending.FinalKill);
    }

    [Fact]
    public void VictoryLapUsesItsOwnCountdownWithoutAdvancingTheWorld()
    {
        Fixture fixture = new(victoryLapTicks: 2);
        fixture.Ending.BeginVictoryLap(fixture.Context, new Victor.Player(1));

        Assert.False(fixture.Ending.AdvanceVictoryLap(fixture.Context));
        Assert.True(fixture.Ending.AdvanceVictoryLap(fixture.Context));
        Assert.Equal(0, fixture.Context.World.Tick);
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

        public ScoredKill WinningKill(Death death)
        {
            Player victim = new(death.PeerId, $"Player {death.PeerId}", 0, 1);
            DeathScore score = new(
                death.KillerId,
                death.PeerId,
                DeathKind.KILL,
                default,
                default,
                null,
                default);
            return new ScoredKill(
                null, victim, score, death.Owned, FirstBlood: false, death.ShellId);
        }
    }
}
