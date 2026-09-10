using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Respawning;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Runtime.Tests.Core.Sim;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class ParticipationStepTests
{
    [Fact]
    public void BlockedReturnCanEnterSpectatingAndLaterPublishAnAuthoritativeSchedule()
    {
        SimWorldTests.ControlledRespawns respawns = new();
        Fixture fixture = new(0, respawns);
        MatchParticipationChange death = Assert.Single(fixture.Kill());
        Assert.Equal(-1, death.State.ReturnTick);
        Assert.True(death.State.IsValid);
        for (int i = 0; i < ParticipationStep.DEATH_VIEW_DURATION_TICKS; i++)
        {
            fixture.World.Step();
            fixture.Advance([]);
        }
        Assert.Equal(MatchActivity.SPECTATING, fixture.Participation.Of(fixture.Player).Activity);
        respawns.Resources = 1;
        respawns.ReturnAt = fixture.World.Tick + 3;
        fixture.World.Step();
        MatchParticipationChange scheduled = Assert.Single(fixture.Advance([]));
        Assert.Equal(fixture.World.Tick + fixture.World.Players[1].RespawnTicks, scheduled.State.ReturnTick);
        Assert.Equal(MatchActivity.SPECTATING, scheduled.State.Activity);

        respawns.Resources = 0;
        fixture.World.Step();
        Assert.Equal(-1, Assert.Single(fixture.Advance([])).State.ReturnTick);
        respawns.Resources = 1;
        fixture.World.Step();
        Assert.Equal(MatchParticipation.Active, Assert.Single(fixture.Advance([])).State);
    }

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
        while (!fixture.World.Players[1].IsAlive)
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

    private sealed class Fixture
    {
        private readonly MatchCells _cells = new();

        public Fixture(int respawnTicks, RespawnStrategy? respawns = null)
        {
            Participation = new ParticipationStep(_cells.Keys);
            Player = _cells.GetOrJoin(1);
            TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
            World = new SimWorld(terrain, new MatchConfig
            {
                Rules = new ModeRules
                {
                    Respawn = new FixedRespawnRules { Delay = (float)respawnTicks / SimConfig.TICK_RATE },
                    SpawnImmunity = 0,
                },
            }, Array.Empty<SpawnPoint>(), respawns: respawns);
            World.AddPlayer(Player.PeerId, team: null);
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
            return Participation.Apply(new MatchContext(World, _cells.Seated), deaths);
        }
    }
}
