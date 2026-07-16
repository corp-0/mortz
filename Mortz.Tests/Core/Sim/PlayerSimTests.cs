using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Sim;

public class PlayerSimTests
{
    private static readonly TerrainMask _flat = TestWorlds.Flat();
    private static readonly PlayerStats _stats = TestWorlds.Stats;

    private static PlayerState NewGroundedPlayer(float x = 200) => new()
    {
        PeerId = 1,
        Position = new Vec2(x, TestWorlds.FLOOR_Y),
        Grounded = true,
    };

    private static readonly PlayerInput _right = new(InputButtons.RIGHT);
    private static readonly PlayerInput _idle = new(InputButtons.NONE);
    private static readonly PlayerInput _jumpHeld = new(InputButtons.JUMP);

    [Fact]
    public void AcceleratesGraduallyTowardMaxSpeed()
    {
        PlayerState p = NewGroundedPlayer(50);

        p = PlayerSim.Tick(p, _right, _flat, _stats);
        Assert.True(p.Velocity.X > 0);
        Assert.True(p.Velocity.X < SimConfig.MAX_RUN_SPEED);

        for (int i = 0; i < SimConfig.TICK_RATE; i++) // one full second
            p = PlayerSim.Tick(p, _right, _flat, _stats);
        Assert.Equal(SimConfig.MAX_RUN_SPEED, p.Velocity.X);
    }

    [Fact]
    public void FrictionStopsRunWhenInputReleased()
    {
        PlayerState p = NewGroundedPlayer(50);
        for (int i = 0; i < 30; i++) p = PlayerSim.Tick(p, _right, _flat, _stats);

        p = PlayerSim.Tick(p, _idle, _flat, _stats);
        Assert.True(p.Velocity.X < SimConfig.MAX_RUN_SPEED); // decelerating, not instant stop
        Assert.True(p.Velocity.X > 0);

        for (int i = 0; i < 60; i++) p = PlayerSim.Tick(p, _idle, _flat, _stats);
        Assert.Equal(0, p.Velocity.X);
    }

    [Fact]
    public void JumpRisesThenLandsBackOnFloor()
    {
        PlayerState p = NewGroundedPlayer();
        p = PlayerSim.Tick(p, _jumpHeld, _flat, _stats);
        Assert.False(p.Grounded);
        Assert.True(p.Velocity.Y < 0);
        Assert.True(p.Position.Y < TestWorlds.FLOOR_Y);

        for (int i = 0; i < 2 * SimConfig.TICK_RATE && !p.Grounded; i++)
            p = PlayerSim.Tick(p, _idle, _flat, _stats);

        Assert.True(p.Grounded);
        Assert.Equal(TestWorlds.FLOOR_Y, p.Position.Y);
        Assert.Equal(0, p.Velocity.Y);
    }

    [Fact]
    public void HoldingJumpDoesNotRejumpOnLanding_EdgeTriggered()
    {
        PlayerState p = NewGroundedPlayer();
        p = PlayerSim.Tick(p, _jumpHeld, _flat, _stats); // takes off

        // Hold jump through the whole arc and past landing.
        for (int i = 0; i < 3 * SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, _jumpHeld, _flat, _stats);

        Assert.True(p.Grounded); // still on the ground: held button didn't re-trigger
    }

    [Fact]
    public void JumpingKeepsHorizontalMomentum()
    {
        PlayerState p = NewGroundedPlayer(50);
        for (int i = 0; i < 60; i++) p = PlayerSim.Tick(p, _right, _flat, _stats);
        float runSpeed = p.Velocity.X;

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.RIGHT | InputButtons.JUMP), _flat, _stats);
        Assert.Equal(runSpeed, p.Velocity.X); // at max speed: unchanged by the jump
        Assert.True(p.Velocity.Y < 0);
    }

    [Fact]
    public void StoppedByWall()
    {
        PlayerState p = NewGroundedPlayer(300);
        for (int i = 0; i < 5 * SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, _right, _flat, _stats);

        Assert.Equal(0, p.Velocity.X);
        // Body edge flush against the wall (within one sub-step of pixel resolution).
        Assert.True(p.Position.X + SimConfig.PLAYER_HALF_WIDTH <= TestWorlds.WALL_RIGHT + 1);
        Assert.True(p.Position.X + SimConfig.PLAYER_HALF_WIDTH > TestWorlds.WALL_RIGHT - 2);
    }

    [Fact]
    public void SmallBump_IsSteppedOverWhileRunning()
    {
        // A 3 px ledge on the floor ahead of the player.
        TerrainMask world = TestWorlds.Flat(extraSolid: (x, y) => x is >= 260 and < 300 && y >= TestWorlds.FLOOR_Y - 3);

        PlayerState p = NewGroundedPlayer(200);
        for (int i = 0; i < 2 * SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, _right, world, _stats);

        Assert.True(p.Position.X > 270); // did not get stuck at the bump edge
        Assert.True(p.Grounded);
    }

    [Fact]
    public void TallWall_IsNotSteppedOver()
    {
        TerrainMask world = TestWorlds.Flat(extraSolid: (x, y) => x is >= 260 and < 300 && y >= TestWorlds.FLOOR_Y - 40);

        PlayerState p = NewGroundedPlayer(200);
        for (int i = 0; i < 2 * SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, _right, world, _stats);

        Assert.True(p.Position.X + SimConfig.PLAYER_HALF_WIDTH <= 261); // stopped at the wall
        Assert.Equal(0, p.Velocity.X);
    }

    [Fact]
    public void Ceiling_StopsUpwardMotion()
    {
        // Slab whose underside is at y=160; jumping from the floor would
        // otherwise reach ~100 px of rise. Feet can't get above 160 + body height.
        TerrainMask world = TestWorlds.Flat(extraSolid: (x, y) => y < 160);
        float minFeetY = float.MaxValue;

        PlayerState p = NewGroundedPlayer();
        p = PlayerSim.Tick(p, _jumpHeld, world, _stats);
        for (int i = 0; i < 2 * SimConfig.TICK_RATE && !p.Grounded; i++)
        {
            p = PlayerSim.Tick(p, _idle, world, _stats);
            minFeetY = MathF.Min(minFeetY, p.Position.Y);
        }

        float lowestPossibleFeet = 160 + SimConfig.PLAYER_HALF_HEIGHT * 2;
        Assert.True(minFeetY >= lowestPossibleFeet - 1);
        Assert.True(p.Grounded); // came back down
    }

    [Fact]
    public void CarvingBelowPlayer_MakesThemFallThrough()
    {
        TerrainMask world = TestWorlds.Flat(destructible: (x, y) => x is >= 150 and < 250 && y is >= 200 and < 230);

        // Stand on top of the destructible platform.
        PlayerState p = NewGroundedPlayer() with { Position = new Vec2(200, 200) };
        p = PlayerSim.Tick(p, _idle, world, _stats);
        Assert.True(p.Grounded);

        world.CarveCircle(200, 215, 40);
        for (int i = 0; i < SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, _idle, world, _stats);

        Assert.Equal(TestWorlds.FLOOR_Y, p.Position.Y); // fell to the real floor
    }
}
