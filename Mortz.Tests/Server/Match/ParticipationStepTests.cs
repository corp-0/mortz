using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class ParticipationStepTests
{
    [Fact]
    public void InitializesSeatsAndJipSpectators()
    {
        MatchCells cells = new();
        ParticipationStep participation = new(cells.Keys);
        Player seated = cells.GetOrJoin(1);
        Player spectator = cells.GetOrJoin(2);

        participation.Seat(seated);
        participation.AddJipSpectator(spectator);

        Assert.Equal(MatchParticipation.Active, participation.Of(seated));
        Assert.Equal(MatchParticipation.JipSpectator, participation.Of(spectator));
    }

    [Fact]
    public void DeathHoldsPresentationBeforeSpectating()
    {
        Fixture fixture = new(respawnTicks: SimConfig.TICK_RATE * 6);

        MatchParticipationChange death = Assert.Single(fixture.Kill());

        Assert.Equal(MatchActivity.DEATH_PRESENTATION, death.State.Activity);
        for (int i = 0; i < ParticipationStep.DEATH_VIEW_DURATION_TICKS - 1; i++)
        {
            fixture.World.Step();
            Assert.Empty(fixture.Advance([]));
        }

        fixture.World.Step();
        MatchParticipationChange spectating = Assert.Single(fixture.Advance([]));
        Assert.Equal(MatchActivity.SPECTATING, spectating.State.Activity);
        Assert.Equal(SpectateReason.RESPAWN, spectating.State.Reason);
        Assert.Equal(death.State.ReturnTick, spectating.State.ReturnTick);
    }

    [Fact]
    public void RespawnReturnsThePlayerToActive()
    {
        Fixture fixture = new(respawnTicks: SimConfig.TICK_RATE * 2);
        fixture.Kill();

        MatchParticipationChange? active = null;
        while (fixture.World.Players[1].RespawnTicks > 0)
        {
            fixture.World.Step();
            foreach (MatchParticipationChange change in fixture.Advance([]))
            {
                if (change.State.Activity == MatchActivity.ACTIVE)
                    active = change;
            }
        }

        Assert.NotNull(active);
        Assert.Equal(MatchParticipation.Active, active.Value.State);
        Assert.Equal(MatchParticipation.Active, fixture.Participation.Of(fixture.Player));
    }

    [Fact]
    public void RequiresSimulationToRunFirst()
    {
        MatchCells cells = new();
        ParticipationStep participation = new(cells.Keys);
        MatchTick tick = NewTick(NewWorld(), cells.Seated);

        Assert.Throws<InvalidOperationException>(() => participation.Advance(tick));
    }

    private static SimWorld NewWorld()
    {
        TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
        return new SimWorld(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
    }

    private static MatchTick NewTick(
        SimWorld world,
        IReadOnlyDictionary<int, Player> seated) =>
        new(new MatchContext(world, seated), default);

    private sealed class Fixture
    {
        private readonly MatchCells _cells = new();

        public Fixture(int respawnTicks)
        {
            Participation = new ParticipationStep(_cells.Keys);
            Player = _cells.GetOrJoin(1);
            TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
            World = new SimWorld(terrain, new MatchConfig
            {
                Rules = new ModeRules
                {
                    RespawnDelay = (float)respawnTicks / SimConfig.TICK_RATE,
                    SpawnImmunity = 0,
                },
            }, Array.Empty<SpawnPoint>());
            World.AddPlayer(Player.PeerId, team: null, skin: Player.Skin);
            Participation.Seat(Player);
        }

        public ParticipationStep Participation { get; }

        public Player Player { get; }

        public SimWorld World { get; }

        public IReadOnlyList<MatchParticipationChange> Kill()
        {
            World.QueueDamage(Player.PeerId, byte.MaxValue);
            World.Step();
            return Advance(World.Deaths);
        }

        public IReadOnlyList<MatchParticipationChange> Advance(IReadOnlyList<Death> deaths)
        {
            MatchTick tick = NewTick(World, _cells.Seated);
            tick.SetSimulationOutputs([], [], [], deaths);
            Participation.Advance(tick);
            return tick.ParticipationChanges;
        }
    }
}
