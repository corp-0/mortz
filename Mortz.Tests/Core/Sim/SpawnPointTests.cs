using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Sim;

public class SpawnPointTests
{
    private static readonly Vec2[] _authored = [new(100, 250), new(300, 250)];

    [Fact]
    public void AuthoredPoints_AreHandedOutByNetSlot_AndCycleWhenTheyRunOut()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, _authored);

        world.AddPlayer(99);
        world.AddPlayer(7);
        world.AddPlayer(42);

        Assert.Equal(new Vec2(100, 250), world.Players[99].Position);
        Assert.Equal(new Vec2(300, 250), world.Players[7].Position);
        Assert.Equal(new Vec2(100, 250), world.Players[42].Position);
        Assert.Equal((byte)1, world.Players[99].NetSlot);
        Assert.Equal((byte)2, world.Players[7].NetSlot);
        Assert.Equal((byte)3, world.Players[42].NetSlot);
    }

    [Fact]
    public void AuthoredPointDoesNotNeedTerrainSupport()
    {
        Vec2 spawn = new(300, 200);
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, [spawn]);

        world.AddPlayer(1);

        Assert.Equal(spawn, world.Players[1].Position);
        Assert.False(world.Players[1].Grounded);
    }

    [Fact]
    public void Respawn_ReusesTheAuthoredPoint_AndFallsIfTheFloorIsGone()
    {
        TerrainMask terrain = new(TestWorlds.WIDTH, TestWorlds.HEIGHT,
            solid: (x, _) => x < TestWorlds.WALL_LEFT || x >= TestWorlds.WALL_RIGHT,
            destructible: (_, y) => y >= TestWorlds.FLOOR_Y);
        Vec2 spawn = new(100, TestWorlds.FLOOR_Y);
        SimWorld world = new(terrain, TestWorlds.NoSpawnProtectionConfig, [spawn]);
        world.AddPlayer(1);

        int sequence = 0;
        Step(world, ref sequence, InputButtons.FIRE, 64); // point-blank shot into floor
        Assert.True(world.Players[1].RespawnTicks > 0);
        Assert.False(PlayerSim.OnGround(terrain, spawn));

        while (world.Players[1].RespawnTicks > 0)
        {
            Step(world, ref sequence, InputButtons.NONE, 64);
        }

        Assert.Equal(spawn, world.Players[1].Position);
        Assert.False(world.Players[1].Grounded);
        Step(world, ref sequence, InputButtons.NONE, 64);
        Assert.True(world.Players[1].Position.Y > spawn.Y);
    }

    [Fact]
    public void MapWithoutPoints_FallsBackToTheColumnSearch()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);

        world.AddPlayer(1);
        world.AddPlayer(2);

        Assert.Equal(new Vec2(241, TestWorlds.FLOOR_Y), world.Players[1].Position);
        Assert.Equal(new Vec2(130, TestWorlds.FLOOR_Y), world.Players[2].Position);
    }

    private static void Step(SimWorld world, ref int sequence, InputButtons buttons, byte aim)
    {
        world.EnqueueInput(1, sequence++, new PlayerInput(buttons, aim));
        world.Step();
    }
}
