using Mortz.Core.Terrain;

namespace Mortz.Core.Sim.Modifiers;

/// <summary>Detection and effects for situational modifiers. Both the server
/// sim and the client predictor call Detect on the same state and terrain,
/// so situational stats need no replication and prediction stays exact.
/// Wiring a new situation is one flag check in Detect plus its entry in
/// AppendModifiers (and the authored values in Modifiers).</summary>
public static class SituationEffects
{
    /// <summary>What applies to this player right now. Dormant until icy and
    /// watery terrain and the special input exist.</summary>
    public static Situations Detect(in PlayerState state, TerrainMask terrain, in PlayerInput input) =>
        Situations.NONE;

    /// <summary>Flag order, so the mul phase composes identically everywhere.</summary>
    public static void AppendModifiers(Situations flags, List<StatsModifier> into)
    {
        if ((flags & Situations.ON_ICE) != 0)
            into.Add(Modifiers.Ice);
        if ((flags & Situations.IN_WATER) != 0)
            into.Add(Modifiers.Water);
        if ((flags & Situations.SPECIAL) != 0)
            into.Add(Modifiers.Special);
    }

    /// <summary>Which effect zones contain this player's body center, one bit
    /// per zone in declaration order. Same detect-on-both-sides contract as
    /// Detect.</summary>
    public static ulong DetectZones(in PlayerState state, MapZones zones)
    {
        IReadOnlyList<MapZone> effectZones = zones.EffectZones;
        if (effectZones.Count == 0)
            return 0;
        Vec2 center = state.Position with
        {
            Y = state.Position.Y - SimConfig.PLAYER_HALF_HEIGHT,
        };
        ulong mask = 0;
        for (int i = 0; i < effectZones.Count; i++)
        {
            if (effectZones[i].Shape.Contains(center))
                mask |= 1UL << i;
        }
        return mask;
    }

    /// <summary>Declaration order, so overlapping zones compose identically
    /// everywhere.</summary>
    public static void AppendZoneModifiers(ulong mask, MapZones zones,
        List<StatsModifier> into)
    {
        for (int i = 0; mask != 0; i++, mask >>= 1)
        {
            if ((mask & 1) != 0)
                into.Add(zones.EffectZones[i].Effects!);
        }
    }

    /// <summary>Resolves one entity stat from the first containing zone that
    /// changes it. Ignores overlaps</summary>
    public static float ResolveFirstZoneStat(Vec2 point, MapZones zones, Stat stat,
        float baseValue)
    {
        foreach (MapZone zone in zones.EffectZones)
        {
            if (!zone.Shape.Contains(point) || zone.Effects == null ||
                !zone.Effects.Changes.Any(change => change.Stat == stat))
                continue;
            float value = baseValue;
            foreach (StatChange change in zone.Effects.Changes)
            {
                if (change.Stat == stat && change.Op == StatOp.ADD)
                    value += change.Value;
            }
            foreach (StatChange change in zone.Effects.Changes)
            {
                if (change.Stat == stat && change.Op == StatOp.MUL)
                    value *= change.Value;
            }
            return StatsPipeline.Clamp(stat, value);
        }
        return baseValue;
    }
}
