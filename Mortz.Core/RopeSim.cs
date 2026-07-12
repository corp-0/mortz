namespace Mortz.Core;

/// <summary>
/// The ninja rope. Press fires the hook toward the aim; it flies until it
/// embeds in terrain or runs out of range. While attached it's a force, not
/// a winch: at full stretch the rope accelerates you toward the anchor, so
/// speed builds the longer the pull works and pumping a swing gains energy,
/// like the LieroX elastic. Press again to let go and keep what you built.
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
            ApplyPull(ref p, dt);
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
        p.RopeLength = MathF.Max(SimConfig.ROPE_MIN_LENGTH, (p.RopePoint - BodyCenter(p)).Length());
    }

    /// <summary>
    /// The whole swing mechanic: stretched to (or past) the rope length, you
    /// get a constant acceleration toward the anchor; slack, you get nothing.
    /// The body is free to overshoot, the force always brings it back. The
    /// rest length also creeps shorter: without that, a vertical rope can't
    /// climb (you'd rise a pixel, go slack and hang bobbing at the floor).
    /// </summary>
    private static void ApplyPull(ref PlayerState p, float dt)
    {
        p.RopeLength = MathF.Max(SimConfig.ROPE_MIN_LENGTH, p.RopeLength - SimConfig.ROPE_SHORTEN_SPEED * dt);

        Vec2 toAnchor = p.RopePoint - BodyCenter(p);
        float distance = toAnchor.Length();
        if (distance < p.RopeLength || distance < 1e-3f)
            return;
        p.Velocity += toAnchor / distance * (SimConfig.ROPE_PULL_ACCEL * dt);
    }

    private static Vec2 BodyCenter(in PlayerState p) =>
        p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };
}
