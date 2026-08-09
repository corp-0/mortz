using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Core.Sim;

/// <summary>
/// Map zones change the sim only while the player's body center is inside the
/// shape, and the predictor composes them exactly like the server: the whole
/// feature rides the situational path, so nothing replicates.
/// </summary>
public class MapZoneSimTests
{
    private static MapZones LowGravity(ZoneShape shape) => new(
        [new MapZone("space", ["space"], shape,
            new StatsModifier(ModifierId.ZONE, Mul(Stat.GRAVITY, 0.1f)))]);

    [Fact]
    public void RotatedShapesUseTheirLocalAxesForContainment()
    {
        ZoneShape ellipse = ZoneShape.Ellipse(100, 100, 60, 10, 90);
        ZoneShape rect = ZoneShape.Rect(80, 95, 40, 10, 90);

        Assert.True(ellipse.Contains(new Vec2(100, 150)));
        Assert.False(ellipse.Contains(new Vec2(150, 100)));
        Assert.True(rect.Contains(new Vec2(100, 110)));
        Assert.False(rect.Contains(new Vec2(120, 100)));
    }

    /// <summary>Whole-sky low-gravity zone: the same jump must carry the zoned
    /// player higher than the unzoned one by the apex.</summary>
    [Fact]
    public void LowGravityZone_SlowsTheFallInsideIt()
    {
        MapZones zones = LowGravity(ZoneShape.Rect(0, 0,
            TestWorlds.WIDTH, (int)TestWorlds.FLOOR_Y));
        SimWorld zoned = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig,
            zones: zones);
        SimWorld plain = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig);
        zoned.AddPlayer(1);
        plain.AddPlayer(1);

        for (int t = 0; t < 20; t++)
        {
            PlayerInput input = new(t == 0 ? InputButtons.JUMP : InputButtons.NONE);
            zoned.EnqueueInput(1, t, input);
            plain.EnqueueInput(1, t, input);
            zoned.Step();
            plain.Step();
        }

        float zonedRise = TestWorlds.FLOOR_Y - zoned.Players[1].Position.Y;
        float plainRise = TestWorlds.FLOOR_Y - plain.Players[1].Position.Y;
        Assert.True(zonedRise > plainRise + 10,
            $"zoned player rose {zonedRise} px, unzoned {plainRise} px");
    }

    /// <summary>One tick of freefall inside the zone gains a tenth of the
    /// gravity a tick outside it does; membership ends at the shape's edge.</summary>
    [Fact]
    public void LeavingTheZone_RestoresBaseStats()
    {
        MapZones zones = LowGravity(ZoneShape.Circle(100, 100, 40));
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig,
            zones: zones);
        w.AddPlayer(1);

        float inside = FreefallTickSpeed(w, at: new Vec2(100, 116), tick: 0);
        float outside = FreefallTickSpeed(w, at: new Vec2(300, 116), tick: 1);

        Assert.True(inside < outside * 0.2f,
            $"gained {inside} px/s inside the zone, {outside} px/s outside");
    }

    /// <summary>The zero-correction property from PredictorTests must survive
    /// a zone the player keeps crossing: predictor and server detect the same
    /// membership and compose the same stats.</summary>
    [Fact]
    public void PredictionMatchesServer_AcrossZoneBoundaries()
    {
        MapZones zones = new(
        [
            new MapZone("space", [], ZoneShape.Rect(0, 0, 200, (int)TestWorlds.FLOOR_Y),
                new StatsModifier(ModifierId.ZONE,
                    Mul(Stat.GRAVITY, 0.2f), Add(Stat.TOTAL_JUMPS, 2))),
            new MapZone("mud", [], ZoneShape.Circle(250, (int)TestWorlds.FLOOR_Y, 60),
                new StatsModifier(ModifierId.ZONE,
                    Mul(Stat.MAX_RUN_SPEED, 0.5f))),
        ]);
        SimWorld server = new SimWorld(TestWorlds.Flat(),
            TestWorlds.NoSpawnProtectionConfig, zones: zones);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(
            server.Terrain, TestWorlds.NoSpawnProtectionConfig, zones);
        predictor.Reconcile(server.Players[1], -1);

        for (int t = 0; t < 300; t++)
        {
            InputButtons buttons = (t / 40) % 2 == 0
                ? InputButtons.RIGHT
                : InputButtons.LEFT;
            if (t % 8 == 0)
                buttons |= InputButtons.JUMP;
            PlayerInput input = new(buttons);
            predictor.LocalTick(input);
            server.EnqueueInput(1, t, input);
            server.Step();

            PlayerState authoritative = server.Players[1];
            Vec2 correction = predictor.Reconcile(authoritative, authoritative.LastInputSeq);

            Assert.Equal(Vec2.Zero, correction);
            Assert.Equal(authoritative.Position, predictor.State.Position);
        }
    }

    [Fact]
    public void MortarGravityChangesOnlyWhileTheShellIsInsideTheZone()
    {
        MapZones zones = new(
        [
            new MapZone("space", [], ZoneShape.Rect(0, 0, 200, 200),
                new StatsModifier(ModifierId.ZONE,
                    Mul(Stat.MORTAR_GRAVITY, 0.1f))),
        ]);
        TerrainMask empty = new(400, 400, (_, _) => false, (_, _) => false);
        MortarState inside = new() { Position = new Vec2(100, 100) };
        MortarState outside = new() { Position = new Vec2(300, 100) };

        MortarSim.Tick(ref inside, empty, TestWorlds.ProductionConfig.Combat,
            SimConfig.DT, zones);
        MortarSim.Tick(ref outside, empty, TestWorlds.ProductionConfig.Combat,
            SimConfig.DT, zones);

        Assert.InRange(inside.Velocity.Y,
            outside.Velocity.Y * 0.09f, outside.Velocity.Y * 0.11f);
    }

    [Fact]
    public void NegativeGravityAcceleratesPlayersAndMortarsUpward()
    {
        MapZones zones = new(
        [
            new MapZone("inverted", [], ZoneShape.Rect(0, 0, 200, 200),
                new StatsModifier(ModifierId.ZONE,
                    Mul(Stat.GRAVITY, -1), Mul(Stat.MORTAR_GRAVITY, -1))),
        ]);
        TerrainMask empty = new(400, 400, (_, _) => false, (_, _) => false);
        SimWorld world = new(empty, TestWorlds.ProductionConfig, zones: zones);
        world.AddPlayer(1);
        world.Teleport(1, new Vec2(100, 116));
        world.EnqueueInput(1, 0, new PlayerInput(InputButtons.NONE));
        world.Step();
        MortarState mortar = new() { Position = new Vec2(100, 100) };
        MortarSim.Tick(ref mortar, empty, TestWorlds.ProductionConfig.Combat,
            SimConfig.DT, zones);

        Assert.True(world.Players[1].Velocity.Y < 0);
        Assert.True(mortar.Velocity.Y < 0);
    }

    /// <summary>Teleports to a mid-air point, steps one tick, and returns the
    /// vertical speed gained: gravity as the sim actually applied it there.</summary>
    private static float FreefallTickSpeed(SimWorld w, Vec2 at, int tick)
    {
        w.Teleport(1, at);
        w.EnqueueInput(1, tick, new PlayerInput(InputButtons.NONE));
        w.Step();
        return w.Players[1].Velocity.Y;
    }
}
