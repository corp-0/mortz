using Mortz.Core.Match;
using Mortz.Core.Terrain;

namespace Mortz.Core.Sim;

/// <summary>
/// Per-tick ballistics of one mortar shell: gravity, then substepped movement
/// so a fast shell can't tunnel through thin terrain. The first solid pixel
/// on the path is the impact point.
/// </summary>
public static class MortarSim
{
    public static MortarOutcome Tick(ref MortarState m, TerrainMask terrain, MatchConfig cfg, float dt)
    {
        if (++m.AgeTicks >= SimConfig.MORTAR_MAX_LIFETIME_TICKS)
            return MortarOutcome.Exploded;

        m.Velocity = m.Velocity with
        {
            Y = MathF.Min(m.Velocity.Y + cfg.MortarGravity * dt, cfg.MortarMaxFall),
        };

        const float SUB_STEP = 4f;
        float distance = m.Velocity.Length() * dt;
        if (distance < 1e-3f)
            return MortarOutcome.Flying;
        Vec2 dir = m.Velocity * (dt / distance);

        for (float moved = 0; moved < distance; moved += SUB_STEP)
        {
            m.Position += dir * MathF.Min(SUB_STEP, distance - moved);
            if (terrain.IsSolid((int)m.Position.X, (int)m.Position.Y))
                return MortarOutcome.Exploded;
            if (OutOfPlay(m.Position, terrain))
                return MortarOutcome.Exploded;
        }
        return MortarOutcome.Flying;
    }

    /// <summary>Above the map the shell keeps flying (OOB is empty and gravity
    /// can bring it back down); crossing a side or the bottom detonates at the
    /// last simulated position.</summary>
    private static bool OutOfPlay(Vec2 pos, TerrainMask terrain) =>
        pos.X < 0 || pos.X >= terrain.Width || pos.Y >= terrain.Height;
}
