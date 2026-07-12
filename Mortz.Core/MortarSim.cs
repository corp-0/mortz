namespace Mortz.Core;

public enum MortarOutcome : byte
{
    Flying = 0,
    /// <summary>Hit terrain; Position is the impact point.</summary>
    Exploded = 1,
    /// <summary>Left the map with nothing left to hit.</summary>
    Fizzled = 2,
}

/// <summary>
/// Per-tick ballistics of one mortar shell: gravity, then substepped movement
/// so a fast shell can't tunnel through thin terrain. The first solid pixel
/// on the path is the impact point.
/// </summary>
public static class MortarSim
{
    public static MortarOutcome Tick(ref MortarState m, TerrainMask terrain, float dt)
    {
        m.Velocity = m.Velocity with
        {
            Y = MathF.Min(m.Velocity.Y + SimConfig.MORTAR_GRAVITY * dt, SimConfig.MORTAR_MAX_FALL),
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
                return MortarOutcome.Fizzled;
        }
        return MortarOutcome.Flying;
    }

    /// <summary>Above the map the shell keeps flying (OOB is empty and gravity
    /// brings it back down); past a side or the bottom it can't hit anything anymore.</summary>
    private static bool OutOfPlay(Vec2 pos, TerrainMask terrain) =>
        pos.X < 0 || pos.X >= terrain.Width || pos.Y >= terrain.Height;
}
