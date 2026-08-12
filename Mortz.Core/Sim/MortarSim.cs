using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.Core.Sim;

public static class MortarSim
{
    public static MortarOutcome Tick(ref MortarState m, TerrainMask terrain, Combat cfg, float dt,
        MapZones? zones = null)
    {
        if (++m.AgeTicks >= SimConfig.MORTAR_MAX_LIFETIME_TICKS)
            return MortarOutcome.EXPLODED;

        float gravity = SituationEffects.ResolveFirstZoneStat(m.Position,
            zones ?? MapZones.None, Stat.MORTAR_GRAVITY, cfg.MortarGravity);
        m.Velocity = m.Velocity with
        {
            Y = MathF.Min(m.Velocity.Y + gravity * dt, cfg.MortarMaxFall),
        };

        float speed = m.Velocity.Length();
        float distance = speed * dt;
        if (distance < 1e-3f)
            return MortarOutcome.FLYING;
        Vec2 dir = m.Velocity / speed;
        Vec2 lastClearCenter = m.Position;

        for (float moved = 0; moved < distance; moved += 1f)
        {
            m.Position += dir * MathF.Min(1f, distance - moved);
            Vec2 nose = m.Position + dir * SimConfig.MORTAR_NOSE_OFFSET;
            int hitX = (int)nose.X;
            int hitY = (int)nose.Y;
            if (terrain.IsSolid(hitX, hitY))
            {
                Vec2 normal = terrain.SurfaceNormal(hitX, hitY, dir);
                float incidence = -Vec2.Dot(dir, normal);
                if (speed >= SimConfig.MORTAR_RICOCHET_MIN_SPEED &&
                    incidence > 0 && incidence < SimConfig.MORTAR_RICOCHET_MAX_INCIDENCE)
                {
                    m.Position = lastClearCenter;
                    m.Velocity = (m.Velocity - normal * (2f * Vec2.Dot(m.Velocity, normal))) *
                                 SimConfig.MORTAR_RICOCHET_SPEED_RETENTION;
                    return MortarOutcome.FLYING;
                }

                // Point-blank shots can spawn with their nose already in terrain.
                m.Position = OutsideContact(terrain, nose, dir);
                return MortarOutcome.EXPLODED;
            }
            if (OutOfPlay(m.Position, terrain))
                return MortarOutcome.EXPLODED;
            lastClearCenter = m.Position;
        }
        return MortarOutcome.FLYING;
    }

    private static Vec2 OutsideContact(TerrainMask terrain, Vec2 nose, Vec2 direction)
    {
        Vec2 contact = nose;
        int limit = terrain.Width + terrain.Height;
        for (int i = 0; i < limit && terrain.IsSolid((int)contact.X, (int)contact.Y); i++)
        {
            contact -= direction;
        }
        return contact;
    }

    /// <summary>Above the map the shell keeps flying (OOB is empty and gravity
    /// can bring it back down); crossing a side or the bottom detonates at the
    /// last simulated position.</summary>
    private static bool OutOfPlay(Vec2 pos, TerrainMask terrain) =>
        pos.X < 0 || pos.X >= terrain.Width || pos.Y >= terrain.Height;
}
