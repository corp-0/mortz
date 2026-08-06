using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Replication;

public class MortarZoneReplicaTests
{
    private static readonly TerrainMask _empty = new(
        400, 400, (_, _) => false, (_, _) => false);

    private static readonly MapZones _zones = new(
    [
        new MapZone("space", [], ZoneShape.Rect(100, 0, 100, 400),
            new StatsModifier(ModifierId.ZONE,
                new StatChange(Stat.MORTAR_GRAVITY, StatOp.MUL, -1))),
    ]);

    [Fact]
    public void RemoteReplica_MatchesZoneAwareTicks()
    {
        MortarState authoritative = new()
        {
            Id = 7,
            Position = new Vec2(250, 100),
            Velocity = new Vec2(-600, 0),
        };
        MortarReplicaSet replicas = new(
            _empty, TestWorlds.ProductionConfig.Combat, _zones);
        replicas.Spawn(authoritative, eventTick: 0, newestServerTick: 0);

        bool wasInside = false;
        bool wasOutsideAfterInside = false;
        for (int tick = 0; tick < 20; tick++)
        {
            Assert.Equal(MortarOutcome.FLYING, MortarSim.Tick(ref authoritative,
                _empty, TestWorlds.ProductionConfig.Combat, SimConfig.DT, _zones));
            replicas.Tick();

            RenderMortar replica = Assert.Single(replicas.Render());
            Assert.Equal(authoritative.Position, replica.Position);
            Assert.Equal(authoritative.Velocity, replica.Velocity);
            bool inside = _zones.All[0].Shape.Contains(authoritative.Position);
            wasInside |= inside;
            wasOutsideAfterInside |= wasInside && !inside;
        }

        Assert.True(wasInside, "the shell never entered the zone");
        Assert.True(wasOutsideAfterInside, "the shell never left the zone");
    }

    [Fact]
    public void RemoteReplica_FastForwardUsesZones()
    {
        MortarState spawn = new()
        {
            Id = 7,
            Position = new Vec2(250, 100),
            Velocity = new Vec2(-600, 0),
        };
        MortarState authoritative = spawn;
        for (int tick = 0; tick < 12; tick++)
        {
            MortarSim.Tick(ref authoritative, _empty,
                TestWorlds.ProductionConfig.Combat, SimConfig.DT, _zones);
        }

        MortarReplicaSet replicas = new(
            _empty, TestWorlds.ProductionConfig.Combat, _zones);
        replicas.Spawn(spawn, eventTick: 10, newestServerTick: 22);

        RenderMortar replica = Assert.Single(replicas.Render());
        Assert.Equal(authoritative.Position, replica.Position);
        Assert.Equal(authoritative.Velocity, replica.Velocity);
    }
}
