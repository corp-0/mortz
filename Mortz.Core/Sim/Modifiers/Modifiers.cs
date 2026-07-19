using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Core.Sim.Modifiers;

/// <summary>The authored modifier table: defining a buff or debuff is one
/// declaration here. Numbers are placeholders until their features land;
/// the shapes are the contract.</summary>
public static class Modifiers
{
    /// <summary>Icy terrain underfoot.</summary>
    public static readonly StatsModifier Ice = new(ModifierId.ICE,
        Mul(Stat.GROUND_FRICTION, 0.2f));

    /// <summary>Submerged: floaty, but one air jump drowns.</summary>
    public static readonly StatsModifier Water = new(ModifierId.WATER,
        Mul(Stat.GRAVITY, 0.4f),
        Add(Stat.TOTAL_JUMPS, -1));

    /// <summary>Held special (shift).</summary>
    public static readonly StatsModifier Special = new(ModifierId.SPECIAL,
        Mul(Stat.MAX_RUN_SPEED, 1.5f));
}
