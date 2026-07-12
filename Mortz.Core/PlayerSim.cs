namespace Mortz.Core;

/// <summary>
/// Per-tick player physics: (state, input, terrain) in, new state out. This
/// has to stay a pure function, prediction replays it over buffered inputs.
/// Movement is a swept AABB against the terrain mask, one pixel at a time per
/// axis, with step-up so small carved bumps don't stop a running player.
/// Position is the feet midpoint; the body occupies [X-hw, X+hw) x [Y-2*hh, Y).
/// </summary>
public static class PlayerSim
{
    public static PlayerState Tick(PlayerState p, PlayerInput input, TerrainMask terrain)
    {
        const float DT = SimConfig.DT;

        if (p.DashCooldown > 0)
            p.DashCooldown--;

        // Horizontal drive.
        float target = input.MoveDir * SimConfig.MAX_RUN_SPEED;
        float rate = input.MoveDir != 0
            ? (p.Grounded ? SimConfig.GROUND_ACCEL : SimConfig.AIR_ACCEL)
            : (p.Grounded ? SimConfig.GROUND_FRICTION : SimConfig.AIR_ACCEL * 0.5f);
        p.Velocity = p.Velocity with { X = MoveToward(p.Velocity.X, target, rate * DT) };

        // Gravity, capped harder while wall sliding.
        int wall = WallDir(terrain, p.Position);
        bool wallSliding = !p.Grounded && p.Velocity.Y > 0 && wall != 0 && input.MoveDir == wall;
        float maxFall = wallSliding ? SimConfig.WALL_SLIDE_MAX_FALL : SimConfig.MAX_FALL_SPEED;
        p.Velocity = p.Velocity with { Y = MathF.Min(p.Velocity.Y + SimConfig.GRAVITY * DT, maxFall) };

        // Jumps: ground (incl. coyote grace), then wall, then air.
        bool jumpPressed = input.Jump && (p.PrevButtons & InputButtons.Jump) == 0;
        if (jumpPressed)
        {
            // On a rope, jump only lets go: dropping out of a swing keeps your
            // momentum and the full jump budget for the fall. Costs the press.
            if (p.Rope == RopeMode.Attached)
            {
                RopeSim.ReleaseAttached(ref p);
            }
            else if (p.Grounded || p.CoyoteTicks > 0)
            {
                p.Velocity = p.Velocity with { Y = -SimConfig.JUMP_SPEED };
                p.Grounded = false;
                p.CoyoteTicks = 0;
                p.JumpsLeft = SimConfig.TOTAL_JUMPS - 1;
            }
            else if (wall != 0)
            {
                p.Velocity = new Vec2(-wall * SimConfig.WALL_JUMP_KICK_X, -SimConfig.WALL_JUMP_SPEED_Y);
                p.JumpsLeft = SimConfig.TOTAL_JUMPS - 1; // a wall jump spends the first jump
            }
            else if (p.JumpsLeft > 0)
            {
                p.Velocity = p.Velocity with { Y = -SimConfig.AIR_JUMP_SPEED };
                p.JumpsLeft--;
            }
        }

        // Dash: an impulse along the held movement keys (8-way), added onto the
        // current velocity. Aim stays free for shooting; no keys held = no dash.
        bool dashPressed = input.Dash && (p.PrevButtons & InputButtons.Dash) == 0;
        if (dashPressed && p.DashCooldown == 0 && input.HeldDir != Vec2.Zero)
        {
            p.Velocity += input.HeldDir * SimConfig.DASH_SPEED;
            p.DashCooldown = SimConfig.DASH_COOLDOWN_TICKS;
        }

        RopeSim.Tick(ref p, input, terrain, DT);

        MoveX(ref p, terrain, p.Velocity.X * DT);
        MoveY(ref p, terrain, p.Velocity.Y * DT);
        p.Grounded = OnGround(terrain, p.Position);
        // A fall can end exactly on the ground without ever hitting the
        // blocked branch, so clear leftover downward speed here.
        if (p.Grounded)
        {
            if (p.Velocity.Y > 0)
                p.Velocity = p.Velocity with { Y = 0 };
            p.JumpsLeft = SimConfig.TOTAL_JUMPS;
            // Coyote grace scales with how fast you leave the ledge.
            float bonusSeconds = MathF.Abs(p.Velocity.X) / 100f * SimConfig.COYOTE_BONUS_PER_100_SPEED;
            p.CoyoteTicks = (byte)Math.Min(
                SimConfig.COYOTE_MAX_TICKS,
                SimConfig.COYOTE_BASE_TICKS + (int)(bonusSeconds * SimConfig.TICK_RATE));
        }
        else if (p.CoyoteTicks > 0)
        {
            p.CoyoteTicks--;
        }

        p.Aim = input.Aim;
        p.PrevButtons = input.Buttons;
        return p;
    }

    /// <summary>Is the body AABB overlapping any solid cell at this feet position?</summary>
    public static bool BodyBlocked(TerrainMask terrain, Vec2 feet) =>
        terrain.RectSolid(
            feet.X - SimConfig.PLAYER_HALF_WIDTH, feet.Y - SimConfig.PLAYER_HALF_HEIGHT * 2,
            feet.X + SimConfig.PLAYER_HALF_WIDTH, feet.Y);

    /// <summary>Standing: solid ground in the pixel row under the feet.</summary>
    public static bool OnGround(TerrainMask terrain, Vec2 feet) =>
        terrain.RectSolid(
            feet.X - SimConfig.PLAYER_HALF_WIDTH, feet.Y,
            feet.X + SimConfig.PLAYER_HALF_WIDTH, feet.Y + 1);

    /// <summary>
    /// Wall contact: -1 (left), +1 (right) or 0. Samples a 1 px strip beside
    /// the body, excluding the lowest pixels so floors don't read as walls.
    /// </summary>
    public static int WallDir(TerrainMask terrain, Vec2 feet)
    {
        float top = feet.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 + 2;
        float bottom = feet.Y - 4;
        if (terrain.RectSolid(feet.X - SimConfig.PLAYER_HALF_WIDTH - 1, top, feet.X - SimConfig.PLAYER_HALF_WIDTH, bottom))
            return -1;
        if (terrain.RectSolid(feet.X + SimConfig.PLAYER_HALF_WIDTH, top, feet.X + SimConfig.PLAYER_HALF_WIDTH + 1, bottom))
            return 1;
        return 0;
    }

    private static void MoveX(ref PlayerState p, TerrainMask terrain, float dx)
    {
        float sign = MathF.Sign(dx);
        for (float remaining = MathF.Abs(dx); remaining > 0; remaining -= 1)
        {
            float step = MathF.Min(1f, remaining);
            Vec2 next = p.Position with { X = p.Position.X + sign * step };
            if (!BodyBlocked(terrain, next))
            {
                p.Position = next;
                continue;
            }
            if (p.Grounded && TryStepUp(terrain, next, out Vec2 lifted))
            {
                p.Position = lifted;
                continue;
            }
            p.Velocity = p.Velocity with { X = 0 };
            break;
        }
    }

    private static bool TryStepUp(TerrainMask terrain, Vec2 blocked, out Vec2 lifted)
    {
        for (int lift = 1; lift <= SimConfig.STEP_UP_PIXELS; lift++)
        {
            lifted = blocked with { Y = blocked.Y - lift };
            if (!BodyBlocked(terrain, lifted))
                return true;
        }
        lifted = default;
        return false;
    }

    private static void MoveY(ref PlayerState p, TerrainMask terrain, float dy)
    {
        float sign = MathF.Sign(dy);
        for (float remaining = MathF.Abs(dy); remaining > 0; remaining -= 1)
        {
            float step = MathF.Min(1f, remaining);
            Vec2 next = p.Position with { Y = p.Position.Y + sign * step };
            if (BodyBlocked(terrain, next))
            {
                p.Velocity = p.Velocity with { Y = 0 }; // land or ceiling bonk
                break;
            }
            p.Position = next;
        }
    }

    private static float MoveToward(float from, float to, float maxDelta) =>
        MathF.Abs(to - from) <= maxDelta ? to : from + MathF.Sign(to - from) * maxDelta;
}
