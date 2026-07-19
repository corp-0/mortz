using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Sim;

/// <summary>Double jump, dash, wall slide and wall jump.</summary>
public class MovementTests
{
    private static readonly TerrainMask _flat = TestWorlds.Flat();
    private static readonly PlayerStats _stats = TestWorlds.Stats;
    private static readonly PlayerInput _idle = new(InputButtons.NONE);
    private static readonly PlayerInput _jumpP = new(InputButtons.JUMP);

    private static PlayerState Grounded(float x = 200) => new()
    {
        PeerId = 1,
        Position = new Vec2(x, TestWorlds.FLOOR_Y),
        Grounded = true,
        JumpsLeft = SimConfig.TOTAL_JUMPS,
    };

    private static PlayerState TickN(PlayerState p, PlayerInput input, TerrainMask world, int n)
    {
        for (int i = 0; i < n; i++)
        {
            p = PlayerSim.Tick(p, input, world, _stats);
        }
        return p;
    }

    [Fact]
    public void DoubleJump_RisesAgainMidAir_ButOnlyOnce()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);          // ground jump
        p = TickN(p, _idle, _flat, 20);                // partway up/over the arc
        float vyBefore = p.Velocity.Y;

        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);          // air jump
        Assert.True(p.Velocity.Y < vyBefore);
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y);
        Assert.Equal(0, p.JumpsLeft);

        p = TickN(p, _idle, _flat, 10);
        float vyMid = p.Velocity.Y;
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);          // third press: nothing left
        Assert.True(p.Velocity.Y >= vyMid);          // gravity only, no new impulse
    }

    [Fact]
    public void AirJump_RefillsOnLanding()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);
        p = TickN(p, _idle, _flat, 5);
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);          // spend the air jump
        Assert.Equal(0, p.JumpsLeft);

        p = TickN(p, _idle, _flat, 4 * SimConfig.TICK_RATE); // land
        Assert.True(p.Grounded);
        Assert.Equal(SimConfig.TOTAL_JUMPS, p.JumpsLeft);
    }

    private static PlayerState RunOffLedge(TerrainMask world)
    {
        PlayerState p = Grounded() with { Position = new Vec2(200, 200) };
        PlayerInput right = new PlayerInput(InputButtons.RIGHT);
        for (int i = 0; i < 2 * SimConfig.TICK_RATE && p.Grounded; i++)
        {
            p = PlayerSim.Tick(p, right, world, _stats);
        }
        return p;
    }

    private static TerrainMask LedgeWorld() =>
        TestWorlds.Flat(extraSolid: (x, y) => x is >= 100 and < 260 && y is >= 200 and < 212);

    [Fact]
    public void CoyoteJump_RightAfterLedge_IsAFullGroundJump()
    {
        TerrainMask world = LedgeWorld();
        PlayerState p = RunOffLedge(world);
        Assert.False(p.Grounded);
        Assert.True(p.CoyoteTicks > 0);

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.RIGHT | InputButtons.JUMP), world, _stats);
        Assert.Equal(-SimConfig.JUMP_SPEED, p.Velocity.Y);        // full jump, not an air jump
        Assert.Equal(SimConfig.TOTAL_JUMPS - 1, p.JumpsLeft);     // spent the first, kept the double
        Assert.Equal(0, p.CoyoteTicks);                          // grace consumed
    }

    [Fact]
    public void FallingWithoutJumping_KeepsBothJumps()
    {
        // Walk off a ledge, let coyote expire: the whole 2-jump budget is
        // still usable mid-air, because none of it was spent getting airborne.
        TerrainMask world = LedgeWorld();
        PlayerState p = RunOffLedge(world);

        for (int i = 0; i < SimConfig.COYOTE_MAX_TICKS + 1; i++)
        {
            p = PlayerSim.Tick(p, _idle, world, _stats);
        }
        Assert.Equal(0, p.CoyoteTicks);
        Assert.Equal(SimConfig.TOTAL_JUMPS, p.JumpsLeft);

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.JUMP), world, _stats); // first air jump
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y);
        Assert.Equal(1, p.JumpsLeft);

        p = TickN(p, _idle, world, 10);
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.JUMP), world, _stats); // second air jump
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y);
        Assert.Equal(0, p.JumpsLeft);

        p = TickN(p, _idle, world, 10);
        float vyBefore = p.Velocity.Y;
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.JUMP), world, _stats); // budget exhausted
        Assert.True(p.Velocity.Y >= vyBefore);
    }

    [Fact]
    public void CoyoteGrace_GrowsWithExitSpeed()
    {
        // Walk off slowly vs. dash off: the dash exit must grant more grace ticks.
        TerrainMask world = LedgeWorld();

        PlayerState slow = Grounded() with { Position = new Vec2(240, 200), Velocity = new Vec2(60, 0) };
        slow = PlayerSim.Tick(slow, _idle, world, _stats); // still on the ledge: grace computed from speed
        byte slowGrace = slow.CoyoteTicks;

        // 900 px/s so that even after one tick of ground friction the grace caps.
        PlayerState fast = Grounded() with { Position = new Vec2(240, 200), Velocity = new Vec2(900, 0) };
        fast = PlayerSim.Tick(fast, new PlayerInput(InputButtons.RIGHT), world, _stats);
        byte fastGrace = fast.CoyoteTicks;

        Assert.True(fastGrace > slowGrace);
        Assert.Equal(SimConfig.COYOTE_MAX_TICKS, fastGrace); // high speed caps out the grace
    }

    [Fact]
    public void GroundJump_SpendsTheFirstJump_KeepsTheDouble()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);
        Assert.Equal(SimConfig.TOTAL_JUMPS - 1, p.JumpsLeft);
    }

    [Fact]
    public void Dash_BurstsAlongHeldKeys_AndCoolsDown()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.RIGHT | InputButtons.DASH), _flat, _stats);
        Assert.True(p.Velocity.X >= SimConfig.DASH_SPEED); // impulse on top of run accel
        Assert.True(p.DashCooldown > 0);

        // Releasing and pressing again during cooldown does nothing.
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.NONE), _flat, _stats);
        float coasting = p.Velocity.X;
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.RIGHT | InputButtons.DASH), _flat, _stats);
        Assert.True(p.Velocity.X < coasting + SimConfig.GROUND_ACCEL * SimConfig.DT + 1);

        // After the cooldown expires a new dash works (re-press edge), any direction.
        p = TickN(p, _idle, _flat, SimConfig.DASH_COOLDOWN_TICKS);
        Assert.Equal(0, p.DashCooldown);
        float before = p.Velocity.X;
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.LEFT | InputButtons.DASH), _flat, _stats);
        Assert.True(p.Velocity.X < before - SimConfig.DASH_SPEED * 0.9f);
    }

    [Fact]
    public void Dash_WithNoKeysHeld_DoesNothing_AndSavesTheCooldown()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.DASH), _flat, _stats);
        Assert.Equal(0, p.Velocity.X);
        Assert.Equal(0, p.DashCooldown); // not spent on a no-direction press
    }

    [Fact]
    public void DiagonalDash_UsesNormalizedEightWayDirection()
    {
        PlayerState p = Grounded();
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.RIGHT | InputButtons.UP | InputButtons.DASH), _flat, _stats);

        float component = SimConfig.DASH_SPEED / MathF.Sqrt(2);
        Assert.True(p.Velocity.X >= component);            // diagonal, not full-speed sideways
        Assert.True(p.Velocity.X < SimConfig.DASH_SPEED);
        Assert.True(p.Velocity.Y < -component + 30);       // upward part (minus one tick of gravity)
    }

    [Fact]
    public void DashWhileFalling_AddsDodgeImpulse_AlongHeldKeys()
    {
        // Falling fast and dashing UP (held key, not aim): the impulse fights
        // the fall, so you dodge upward while your aim stays wherever it was.
        PlayerState p = Grounded() with { Position = new Vec2(200, 100), Grounded = false };
        p = TickN(p, _idle, _flat, 15);
        Assert.True(p.Velocity.Y > 300);

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.UP | InputButtons.DASH), _flat, _stats);
        Assert.True(p.Velocity.Y < 0);      // now moving up
        Assert.Equal(0, p.Velocity.X);      // no sideways drift toward anything
    }

    [Fact]
    public void WallSlide_CapsFallSpeed_WhilePressingIntoWall()
    {
        // Fall alongside the right arena wall, holding right.
        PlayerState p = Grounded(TestWorlds.WALL_RIGHT - SimConfig.PLAYER_HALF_WIDTH) with
        {
            Position = new Vec2(TestWorlds.WALL_RIGHT - SimConfig.PLAYER_HALF_WIDTH, 120),
            Grounded = false,
        };
        PlayerInput pressRight = new PlayerInput(InputButtons.RIGHT);
        p = TickN(p, pressRight, _flat, 30);

        Assert.True(p.Velocity.Y <= SimConfig.WALL_SLIDE_MAX_FALL + 0.01f);

        // Same fall without pressing into the wall: much faster (20 ticks so it
        // can't reach the floor and zero out).
        PlayerState q = p with { Position = new Vec2(200, 120), Velocity = Vec2.Zero };
        q = TickN(q, _idle, _flat, 20);
        Assert.True(q.Velocity.Y > SimConfig.WALL_SLIDE_MAX_FALL * 2);
    }

    [Fact]
    public void WallJump_KicksAwayFromWall_AndRefillsAirJump()
    {
        PlayerState p = Grounded() with
        {
            Position = new Vec2(TestWorlds.WALL_RIGHT - SimConfig.PLAYER_HALF_WIDTH, 120),
            Grounded = false,
            JumpsLeft = 0,
        };
        p = PlayerSim.Tick(p, _jumpP, _flat, _stats);

        Assert.Equal(-SimConfig.WALL_JUMP_KICK_X, p.Velocity.X); // pushed left, away from right wall
        Assert.Equal(-SimConfig.WALL_JUMP_SPEED_Y, p.Velocity.Y);
        Assert.Equal(SimConfig.TOTAL_JUMPS - 1, p.JumpsLeft); // budget reset, first jump spent
    }
}
