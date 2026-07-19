namespace Mortz.Core.Sim.Modifiers;

/// <summary>Identity for add/remove: a source only ever removes its own id,
/// and re-adding an id replaces the previous entry.</summary>
public enum ModifierId : byte
{
    ICE,
    WATER,
    SPECIAL,
}
