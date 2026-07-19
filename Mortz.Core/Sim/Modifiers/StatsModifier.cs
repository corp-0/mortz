namespace Mortz.Core.Sim.Modifiers;

/// <summary>A named, removable set of stat changes. Pure data: removal never
/// subtracts anything, the pipeline just recomputes from base without it, so
/// modifiers compose and undo correctly no matter what else is active.</summary>
public sealed class StatsModifier
{
    public ModifierId Id { get; }
    public IReadOnlyList<StatChange> Changes { get; }

    public StatsModifier(ModifierId id, params StatChange[] changes)
    {
        Id = id;
        Changes = changes;
    }
}
