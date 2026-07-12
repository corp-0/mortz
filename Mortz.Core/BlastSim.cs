namespace Mortz.Core;

/// <summary>
/// Blast damage falloff, server-side only (damage is never predicted).
/// Distance is blast center to the nearest point of the body box: full damage
/// inside the core, linear falloff to the rim, nothing beyond it.
/// </summary>
public static class BlastSim
{
    public static int Damage(in PlayerState p, Vec2 center)
    {
        float d = DistanceToBody(p, center);
        float core = SimConfig.MORTAR_CARVE_RADIUS * SimConfig.BLAST_CORE_FRACTION;
        if (d <= core)
            return SimConfig.MORTAR_DAMAGE;
        if (d > SimConfig.MORTAR_CARVE_RADIUS)
            return 0;
        float t = (d - core) / (SimConfig.MORTAR_CARVE_RADIUS - core);
        return (int)MathF.Round(SimConfig.MORTAR_DAMAGE + (SimConfig.BLAST_EDGE_DAMAGE - SimConfig.MORTAR_DAMAGE) * t);
    }

    private static float DistanceToBody(in PlayerState p, Vec2 center)
    {
        float nx = Math.Clamp(center.X, p.Position.X - SimConfig.PLAYER_HALF_WIDTH, p.Position.X + SimConfig.PLAYER_HALF_WIDTH);
        float ny = Math.Clamp(center.Y, p.Position.Y - SimConfig.PLAYER_HALF_HEIGHT * 2, p.Position.Y);
        return (center - new Vec2(nx, ny)).Length();
    }
}
