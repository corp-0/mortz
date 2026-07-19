using Mortz.Core.Match;

namespace Mortz.Core.Sim.Modifiers;

/// <summary>
/// Composes a player's effective stats: base config, plus every active
/// modifier, re-clamped, then PlayerStats' usual tick/byte resolve. All
/// math happens in config units BEFORE any casting, and both sides of the
/// wire run this same code on the same replicated inputs, so server and
/// prediction stay bit-identical. Per stat: value = (base + every add)
/// * each mul in list order. Adds apply before muls regardless of list
/// order; the mul phase follows list order (float multiplication is not
/// associative), so callers keep lists canonically ordered: persistent
/// modifiers sorted by id, situational appended in flag order.
/// The Stat-to-property Get/Set mapping lives in the generated half
/// (ConfigGenerator).
/// </summary>
public static partial class StatsPipeline
{
    public static PlayerStats Resolve(MatchConfig cfg, IReadOnlyList<StatsModifier> modifiers)
    {
        if (modifiers.Count == 0)
            return PlayerStats.Resolve(cfg);
        MatchConfig modified = MatchConfig.FromBytes(cfg.ToBytes());
        Apply(modified, modifiers, StatOp.ADD);
        Apply(modified, modifiers, StatOp.MUL);
        modified.Clamp();
        return PlayerStats.Resolve(modified);
    }

    private static void Apply(MatchConfig cfg, IReadOnlyList<StatsModifier> modifiers, StatOp phase)
    {
        foreach (StatsModifier modifier in modifiers)
        {
            foreach (StatChange change in modifier.Changes)
            {
                if (change.Op != phase)
                    continue;
                float value = Get(cfg, change.Stat);
                Set(cfg, change.Stat,
                    phase == StatOp.ADD ? value + change.Value : value * change.Value);
            }
        }
    }
}
