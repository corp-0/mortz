using Mortz.Core;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core;

public class SpawnPointTests
{
    private static readonly Vec2[] _authored = [new(100, 250), new(300, 250)];

    [Fact]
    public void Validator_ReportsWhichPointIsWrong_AndWhy()
    {
        TerrainMask terrain = TestWorlds.Flat(extraSolid: (x, y) => x is >= 180 and < 220 && y >= 200);
        Vec2[] points =
        [
            new(100, 250), // valid
            new(10, 250),  // body outside left edge
            new(200, 250), // body overlaps pillar
            new(300, 200), // unsupported
            new(100, 250), // duplicates entry 0
        ];

        IReadOnlyList<SpawnPointValidationError> errors = SpawnPointValidator.Validate(terrain, points);

        Assert.Equal([1, 2, 3, 4], errors.Select(error => error.Index).ToArray());
        Assert.Equal(points[1], errors[0].Position);
        Assert.Contains("out of bounds", errors[0].Reason, StringComparison.Ordinal);
        Assert.Contains("overlaps", errors[1].Reason, StringComparison.Ordinal);
        Assert.Contains("not supported", errors[2].Reason, StringComparison.Ordinal);
        Assert.Contains("duplicates spawn_points[0]", errors[3].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredPoints_AreHandedOutByNetSlot_AndCycleWhenTheyRunOut()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, seed: 1, _authored);

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
    public void Respawn_ReusesTheAuthoredPoint_AndFallsIfTheFloorIsGone()
    {
        TerrainMask terrain = new(TestWorlds.WIDTH, TestWorlds.HEIGHT,
            solid: (x, _) => x < TestWorlds.WALL_LEFT || x >= TestWorlds.WALL_RIGHT,
            destructible: (_, y) => y >= TestWorlds.FLOOR_Y);
        Vec2 spawn = new(100, TestWorlds.FLOOR_Y);
        SimWorld world = new(terrain, TestWorlds.NoSpawnProtectionConfig, seed: 1, [spawn]);
        world.AddPlayer(1);

        int sequence = 0;
        Step(world, ref sequence, InputButtons.Fire, 64); // point-blank shot into floor
        Assert.True(world.Players[1].RespawnTicks > 0);
        Assert.False(PlayerSim.OnGround(terrain, spawn));

        while (world.Players[1].RespawnTicks > 0)
            Step(world, ref sequence, InputButtons.None, 64);

        Assert.Equal(spawn, world.Players[1].Position);
        Assert.False(world.Players[1].Grounded);
        Step(world, ref sequence, InputButtons.None, 64);
        Assert.True(world.Players[1].Position.Y > spawn.Y);
    }

    [Fact]
    public void MapWithoutPoints_FallsBackToTheColumnSearch()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, seed: 1);

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
