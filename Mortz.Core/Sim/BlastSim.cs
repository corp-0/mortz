using Mortz.Core.Terrain;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.Core.Sim;

/// <summary>
/// Blast damage falloff, server-side only (damage is never predicted).
/// Distance is blast center to the nearest point of the body box: full damage
/// inside the core, linear falloff to the rim, nothing beyond it.
/// </summary>
public static class BlastSim
{
    public static int Damage(in PlayerState p, Vec2 center, Combat cfg)
    {
        float d = DistanceToBody(p, center);
        float core = cfg.MortarCarveRadius * cfg.BlastCoreFraction;
        if (d <= core)
            return cfg.MortarDamage;
        if (d > cfg.MortarCarveRadius)
            return 0;
        float t = (d - core) / (cfg.MortarCarveRadius - core);
        return (int)MathF.Round(cfg.MortarDamage + (cfg.BlastEdgeDamage - cfg.MortarDamage) * t);
    }

    public static bool Reaches(in PlayerState p, Vec2 center, TerrainMask terrain)
    {
        float left = p.Position.X - SimConfig.PLAYER_HALF_WIDTH;
        float right = p.Position.X + SimConfig.PLAYER_HALF_WIDTH;
        float top = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2;
        float bottom = p.Position.Y;
        float nearestX = Math.Clamp(center.X, left, right);
        float nearestY = Math.Clamp(center.Y, top, bottom);
        Vec2[] samples =
        [
            new(nearestX, nearestY),
            p.Position with { Y = p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT },
            new(left, top),
            new(right, top),
            new(left, bottom),
            new(right, bottom),
        ];

        foreach (Vec2 sample in samples)
        {
            if (!terrain.SolidBetween(center.X, center.Y, sample.X, sample.Y))
                return true;
        }

        return false;
    }

    private static float DistanceToBody(in PlayerState p, Vec2 center)
    {
        float nx = Math.Clamp(center.X, p.Position.X - SimConfig.PLAYER_HALF_WIDTH, p.Position.X + SimConfig.PLAYER_HALF_WIDTH);
        float ny = Math.Clamp(center.Y, p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2, p.Position.Y);
        return (center - new Vec2(nx, ny)).Length();
    }
}
