namespace Mortz.Core.Sim.Modifiers;

/// <summary>Identity for add/remove: a source only ever removes its own id,
/// and re-adding an id replaces the previous entry.</summary>
public enum ModifierId : byte
{
    ICE,
    WATER,
    SPECIAL,
    /// <summary>Map zone effects; computed from map data on both sides, so
    /// every zone shares this one id instead of replicating or persisting.</summary>
    ZONE,
}
