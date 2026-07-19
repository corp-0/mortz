namespace Mortz.Core.Sim.Modifiers;

/// <summary>One stat, one operation, one number. Three primitives, so any
/// modifier crosses the wire for free and cannot hide logic.</summary>
public readonly record struct StatChange(Stat Stat, StatOp Op, float Value)
{
    public static StatChange Add(Stat stat, float value) => new(stat, StatOp.ADD, value);
    public static StatChange Mul(Stat stat, float value) => new(stat, StatOp.MUL, value);
}
