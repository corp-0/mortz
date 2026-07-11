using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class RopeTests
{
    // Flat world plus a solid ceiling slab at y < 60 to hook onto.
    private static TerrainMask CeilingWorld() => TestWorlds.Flat(extraSolid: (x, y) => y < 60);

    private static readonly byte AimUp = PlayerInput.AimFromVector(new Vec2(0, -1));
    private static readonly PlayerInput Idle = new(InputButtons.None);
    private static readonly PlayerInput FireUp = new(InputButtons.Rope, AimUp);

    private static PlayerState Grounded() => new()
    {
        PeerId = 1,
        Position = new Vec2(200, TestWorlds.FLOOR_Y),
        Grounded = true,
        JumpsLeft = SimConfig.TOTAL_JUMPS,
    };

    private static PlayerState TickUntil(PlayerState p, TerrainMask world, Func<PlayerState, bool> done, int maxTicks = 300)
    {
        for (int i = 0; i < maxTicks && !done(p); i++)
            p = PlayerSim.Tick(p, Idle, world);
        return p;
    }

    [Fact]
    public void Hook_FiredAtCeiling_Attaches()
    {
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        Assert.Equal(RopeMode.Flying, p.Rope);

        p = TickUntil(p, world, s => s.Rope != RopeMode.Flying, 60);
        Assert.Equal(RopeMode.Attached, p.Rope);
        Assert.True(p.RopePoint.Y <= 60); // embedded in the ceiling slab
        Assert.True(p.Velocity.Y < 0);    // attach tug pulls upward
    }

    [Fact]
    public void Hook_FiredIntoOpenSky_FizzlesAtMaxRange()
    {
        TerrainMask world = TestWorlds.Flat();
        byte aimUpRight = PlayerInput.AimFromVector(new Vec2(1, -0.2f).Normalized());
        PlayerState p = PlayerSim.Tick(Grounded(), new PlayerInput(InputButtons.Rope, aimUpRight), world);
        Assert.Equal(RopeMode.Flying, p.Rope);

        p = TickUntil(p, world, s => s.Rope != RopeMode.Flying, 120);
        // At this shallow angle the right wall is within range, so the hook
        // either embedded in the wall or fizzled in the air. Both are fine,
        // grabbing thin air is not.
        if (p.Rope == RopeMode.Attached)
            Assert.True(p.RopePoint.X >= TestWorlds.WALL_RIGHT);
        else
            Assert.Equal(RopeMode.None, p.Rope);
    }

    [Fact]
    public void AttachedRope_ReelsPlayerUpward()
    {
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);

        float startY = p.Position.Y;
        for (int i = 0; i < 2 * SimConfig.TICK_RATE; i++)
            p = PlayerSim.Tick(p, Idle, world);

        Assert.True(p.Position.Y < startY - 50); // reeled well off the floor
        Assert.False(p.Grounded);
    }

    [Fact]
    public void Release_KeepsMomentum()
    {
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);
        for (int i = 0; i < 60; i++)
            p = PlayerSim.Tick(p, Idle, world); // reeling upward, gaining speed

        Vec2 velocityBefore = p.Velocity;
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world); // press again = release

        Assert.Equal(RopeMode.None, p.Rope);
        // Velocity unchanged by the release itself (only this tick's gravity applied).
        Assert.Equal(velocityBefore.X, p.Velocity.X, 1f);
        Assert.True(MathF.Abs(p.Velocity.Y - velocityBefore.Y) < SimConfig.GRAVITY * SimConfig.DT * 2);
    }

    [Fact]
    public void Jump_ReleasesAttachedRope_AndStillJumps()
    {
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);
        // Hang long enough for coyote grace to expire but nowhere near the ceiling.
        for (int i = 0; i < 15; i++)
            p = PlayerSim.Tick(p, Idle, world);

        Assert.Equal(RopeMode.Attached, p.Rope);
        Assert.False(p.Grounded);
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Jump), world);

        Assert.Equal(RopeMode.None, p.Rope);                 // jump let go of the rope
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y); // and the air jump fired
    }

    [Fact]
    public void RopeRelease_FallingPlayer_StillHasBothJumps()
    {
        // Rope to the ceiling, release, fall: neither jump was spent getting
        // airborne, so both presses must work mid-fall.
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);
        for (int i = 0; i < 20; i++)
            p = PlayerSim.Tick(p, Idle, world); // reel up, coyote long expired

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world); // release
        Assert.Equal(RopeMode.None, p.Rope);
        Assert.False(p.Grounded);
        Assert.Equal(SimConfig.TOTAL_JUMPS, p.JumpsLeft);

        for (int i = 0; i < 10; i++)
            p = PlayerSim.Tick(p, Idle, world); // falling

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Jump), world);
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y);
        Assert.Equal(1, p.JumpsLeft);

        for (int i = 0; i < 10; i++)
            p = PlayerSim.Tick(p, Idle, world);

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Jump), world);
        Assert.Equal(-SimConfig.AIR_JUMP_SPEED, p.Velocity.Y);
        Assert.Equal(0, p.JumpsLeft);
    }

    [Fact]
    public void Release_HasShortCooldown_MissHasLongOne()
    {
        TerrainMask world = CeilingWorld();

        // Attach then release: short cooldown.
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world);
        Assert.Equal(RopeMode.None, p.Rope);
        Assert.Equal(SimConfig.ROPE_RELEASE_COOLDOWN_TICKS, p.RopeCooldown);

        // Whiff into open sky: long cooldown.
        TerrainMask flat = TestWorlds.Flat();
        PlayerState q = PlayerSim.Tick(Grounded(), FireUp, flat);
        q = TickUntil(q, flat, s => s.Rope == RopeMode.None, 120);
        Assert.Equal(SimConfig.ROPE_MISS_COOLDOWN_TICKS, q.RopeCooldown);
        Assert.True(q.RopeCooldown > SimConfig.ROPE_RELEASE_COOLDOWN_TICKS);
    }

    [Fact]
    public void Cooldown_BlocksRefire_UntilItExpires()
    {
        TerrainMask world = CeilingWorld();
        PlayerState p = PlayerSim.Tick(Grounded(), FireUp, world);
        p = TickUntil(p, world, s => s.Rope == RopeMode.Attached, 60);
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world); // release

        p = PlayerSim.Tick(p, Idle, world);                                      // button up
        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world); // re-press too soon
        Assert.Equal(RopeMode.None, p.Rope);

        for (int i = 0; i < SimConfig.ROPE_RELEASE_COOLDOWN_TICKS; i++)
            p = PlayerSim.Tick(p, Idle, world);
        Assert.Equal(0, p.RopeCooldown);

        p = PlayerSim.Tick(p, new PlayerInput(InputButtons.Rope, AimUp), world); // now it fires
        Assert.Equal(RopeMode.Flying, p.Rope);
    }

    [Fact]
    public void ReelingIntoObstacle_HoldsBelow_NeverTeleportsPast()
    {
        // Anchor in the ceiling, a solid slab between player and anchor. The
        // reel should press the player against the slab's underside and hold
        // there. Regression: reel tension used to build up for a second and
        // then teleport the player through to the other side.
        TerrainMask world = TestWorlds.Flat(extraSolid: (x, y) =>
            y < 60 ||                                        // ceiling
            (x is >= 160 and < 240 && y is >= 140 and < 150)); // slab in the way

        PlayerState p = Grounded() with
        {
            Position = new Vec2(200, 230),
            Grounded = false,
            Rope = RopeMode.Attached,
            RopePoint = new Vec2(200, 58),
            RopeLength = 156,
        };

        for (int i = 0; i < 3 * SimConfig.TICK_RATE; i++)
        {
            p = PlayerSim.Tick(p, Idle, world);
            // Body top may touch the slab bottom (feet at 182) but never pass it.
            Assert.True(p.Position.Y >= 181, $"teleported through the slab at tick {i}: Y={p.Position.Y}");
        }

        Assert.Equal(RopeMode.Attached, p.Rope);
        // The rope paid out instead of storing impossible reel.
        float distance = (p.RopePoint - new Vec2(p.Position.X, p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT)).Length();
        Assert.True(p.RopeLength >= distance - 1);
    }

    [Fact]
    public void TautRope_RemovesRadialVelocity_KeepsTangential()
    {
        // Hanging straight below the anchor with velocity (200, 300): the
        // downward part is radial (away from the anchor) and must vanish; the
        // sideways part is tangential and must survive. Swinging is this rule
        // applied every tick.
        TerrainMask world = CeilingWorld();
        PlayerState p = Grounded() with
        {
            Position = new Vec2(200, 178), // body center (200,162), anchor 104 px above
            Grounded = false,
            Rope = RopeMode.Attached,
            RopePoint = new Vec2(200, 58),
            RopeLength = 104,
            Velocity = new Vec2(200, 300),
        };

        p = PlayerSim.Tick(p, Idle, world);

        Assert.True(p.Velocity.Y <= 1);    // radial (downward) component killed
        Assert.True(p.Velocity.X > 150);   // tangential survives (minus air drag)
    }

    [Fact]
    public void SnapshotRoundTrips_RopeStates()
    {
        PlayerState[] players = new[]
        {
            new PlayerState { PeerId = 1, Rope = RopeMode.None },
            new PlayerState { PeerId = 2, Rope = RopeMode.Flying, RopePoint = new Vec2(10, 20), RopeVelocity = new Vec2(900, -900) },
            new PlayerState { PeerId = 3, Rope = RopeMode.Attached, RopePoint = new Vec2(5, 6), RopeLength = 77 },
        };

        Snapshot restored = Snapshot.Deserialize(new Snapshot(42, players).Serialize());

        Assert.Equal(RopeMode.None, restored.Players[0].Rope);
        Assert.Equal(new Vec2(10, 20), restored.Players[1].RopePoint);
        Assert.Equal(new Vec2(900, -900), restored.Players[1].RopeVelocity);
        Assert.Equal(new Vec2(5, 6), restored.Players[2].RopePoint);
        Assert.Equal(77, restored.Players[2].RopeLength);
    }
}
