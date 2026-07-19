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
}
