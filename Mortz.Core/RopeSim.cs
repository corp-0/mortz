namespace Mortz.Core;

/// <summary>
/// The ninja rope. Press fires the hook toward the aim; it flies until it
/// embeds in terrain or runs out of range. While attached it keeps reeling
/// shorter and works like a pendulum: when taut, we kill the part of your
/// velocity that points away from the anchor and keep the rest. That one
/// rule is where swinging comes from. Press again to let go and fly off
/// with whatever speed you built up.
/// </summary>
public static class RopeSim
{
    public static void Tick(ref PlayerState p, PlayerInput input, TerrainMask terrain, float dt)
    {
        if (p.RopeCooldown > 0)
            p.RopeCooldown--;

        bool ropePressed = input.Rope && (p.PrevButtons & InputButtons.Rope) == 0;
        if (ropePressed)
        {
            if (p.Rope == RopeMode.None && p.RopeCooldown == 0)
                Fire(ref p, input);
            else if (p.Rope == RopeMode.Flying)
                Miss(ref p); // aborting the throw costs the same as whiffing it
            else if (p.Rope == RopeMode.Attached)
                ReleaseAttached(ref p);
        }

        if (p.Rope == RopeMode.Flying)
            FlyHook(ref p, terrain, dt);

        if (p.Rope == RopeMode.Attached)
            ApplyConstraint(ref p, dt);
    }

    private static void Fire(ref PlayerState p, PlayerInput input)
    {
        p.Rope = RopeMode.Flying;
        p.RopePoint = BodyCenter(p);
        p.RopeVelocity = input.AimDir * SimConfig.ROPE_SPEED;
    }

    /// <summary>Let go of an attached rope: keeps velocity, short re-fire cooldown.</summary>
    public static void ReleaseAttached(ref PlayerState p)
    {
        Clear(ref p);
        p.RopeCooldown = SimConfig.ROPE_RELEASE_COOLDOWN_TICKS;
    }

    private static void Miss(ref PlayerState p)
    {
        Clear(ref p);
        p.RopeCooldown = SimConfig.ROPE_MISS_COOLDOWN_TICKS;
    }

    private static void Clear(ref PlayerState p)
    {
        p.Rope = RopeMode.None;
        p.RopeVelocity = Vec2.Zero;
        p.RopeLength = 0;
    }

    private static void FlyHook(ref PlayerState p, TerrainMask terrain, float dt)
    {
        // Substep so the fast hook can't tunnel through thin terrain.
        const float SUB_STEP = 4f;
        float distance = SimConfig.ROPE_SPEED * dt;
        Vec2 dir = p.RopeVelocity * (1f / SimConfig.ROPE_SPEED);

        for (float moved = 0; moved < distance; moved += SUB_STEP)
        {
            p.RopePoint += dir * MathF.Min(SUB_STEP, distance - moved);
            if (!terrain.Contains((int)p.RopePoint.X, (int)p.RopePoint.Y))
            {
                Miss(ref p); // left the map, nothing to grab out there
                return;
            }
            if (terrain.IsSolid((int)p.RopePoint.X, (int)p.RopePoint.Y))
            {
                Attach(ref p);
                return;
            }
        }
        if ((p.RopePoint - BodyCenter(p)).Length() > SimConfig.ROPE_MAX_RANGE)
            Miss(ref p); // out of range: fizzle, long cooldown
    }

    private static void Attach(ref PlayerState p)
    {
        p.Rope = RopeMode.Attached;
        p.RopeVelocity = Vec2.Zero;
        Vec2 toAnchor = p.RopePoint - BodyCenter(p);
        p.RopeLength = MathF.Max(SimConfig.ROPE_MIN_LENGTH, toAnchor.Length());
        p.Velocity += toAnchor.Normalized() * SimConfig.ROPE_ATTACH_IMPULSE; // the tug
    }

    /// <summary>Reel in, and while the rope is taut, kill any velocity moving
    /// away from the anchor. Keeping the sideways part is what makes swings work.</summary>
    private static void ApplyConstraint(ref PlayerState p, float dt)
    {
        p.RopeLength = MathF.Max(SimConfig.ROPE_MIN_LENGTH, p.RopeLength - SimConfig.ROPE_REEL_SPEED * dt);

        Vec2 toAnchor = p.RopePoint - BodyCenter(p);
        float distance = toAnchor.Length();
        if (distance <= p.RopeLength || distance < 1e-3f)
            return; // slack rope constrains nothing

        Vec2 dir = toAnchor / distance;
        float outward = -Vec2.Dot(p.Velocity, dir); // speed moving away from the anchor
        if (outward > 0)
            p.Velocity += dir * outward; // kill radial, keep tangential
    }

    /// <summary>
    /// After movement: if we're still stretched past the rope length, pull the
    /// body toward the anchor one pixel at a time, stopping at terrain (walls
    /// beat rope). When something blocks the pull, let the rope length back
    /// out to the real distance. Otherwise the reel keeps winding up tension
    /// it can't spend and dumps it as a teleport once a spot past the
    /// obstacle happens to be free.
    /// </summary>
    public static void ConstrainPosition(ref PlayerState p, TerrainMask terrain)
    {
        if (p.Rope != RopeMode.Attached)
            return;

        Vec2 toAnchor = p.RopePoint - BodyCenter(p);
        float distance = toAnchor.Length();
        float excess = distance - p.RopeLength;
        if (excess <= 0)
            return;

        Vec2 dir = toAnchor / distance;
        for (float moved = 0; moved < excess; moved += 1)
        {
            Vec2 candidate = p.Position + dir * MathF.Min(1f, excess - moved);
            if (PlayerSim.BodyBlocked(terrain, candidate))
                break;
            p.Position = candidate;
        }

        float actual = (p.RopePoint - BodyCenter(p)).Length();
        if (actual > p.RopeLength)
            p.RopeLength = actual;
    }

    private static Vec2 BodyCenter(in PlayerState p) =>
        p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };
}
