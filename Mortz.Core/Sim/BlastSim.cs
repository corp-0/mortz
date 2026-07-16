using Mortz.Core.Match;

namespace Mortz.Core.Sim;

/// <summary>
/// Blast damage falloff, server-side only (damage is never predicted).
/// Distance is blast center to the nearest point of the body box: full damage
/// inside the core, linear falloff to the rim, nothing beyond it.
/// </summary>
public static class BlastSim
{
    public static int Damage(in PlayerState p, Vec2 center, MatchConfig cfg)
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

    private static float DistanceToBody(in PlayerState p, Vec2 center)
    {
        float nx = Math.Clamp(center.X, p.Position.X - SimConfig.PLAYER_HALF_WIDTH, p.Position.X + SimConfig.PLAYER_HALF_WIDTH);
        float ny = Math.Clamp(center.Y, p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2, p.Position.Y);
        return (center - new Vec2(nx, ny)).Length();
    }
}
