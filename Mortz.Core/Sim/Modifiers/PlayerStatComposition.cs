using Mortz.Core.Match.Configuration;

namespace Mortz.Core.Sim.Modifiers;

/// <summary>Resolves persistent, situational, and map-zone modifiers in deterministic order.</summary>
public static class PlayerStatComposition
{
    public static PlayerStats ResolveEffective(
        MatchConfig config,
        IReadOnlyList<StatsModifier> persistent,
        Situations situations,
        ulong zoneMask,
        MapZones zones)
    {
        PlayerStats persistentStats = StatsPipeline.Resolve(config, persistent);
        return ResolveEffective(config, persistent, persistentStats, situations, zoneMask, zones);
    }

    public static PlayerStats ResolveEffective(
        MatchConfig config,
        IReadOnlyList<StatsModifier> persistent,
        PlayerStats persistentStats,
        Situations situations,
        ulong zoneMask,
        MapZones zones)
    {
        if (situations == Situations.NONE && zoneMask == 0)
            return persistentStats;

        List<StatsModifier> all = new(persistent);
        SituationEffects.AppendModifiers(situations, all);
        SituationEffects.AppendZoneModifiers(zoneMask, zones, all);
        return StatsPipeline.Resolve(config, all);
    }
}
