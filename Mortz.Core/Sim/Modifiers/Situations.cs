namespace Mortz.Core.Sim.Modifiers;

/// <summary>Tier 2: ephemeral, state-derived conditions. Re-detected every
/// tick, so nothing is ever added or removed and nothing can leak: step off
/// the ice and the effect is gone next tick.</summary>
[Flags]
public enum Situations : byte
{
    NONE = 0,
    ON_ICE = 1 << 0,
    IN_WATER = 1 << 1,
    SPECIAL = 1 << 2,
}
