using JetBrains.Annotations;

namespace Mortz.Core.Match;

/// <summary>
/// Declares a MatchConfig property as a per-player stat: the single source
/// ConfigGenerator expands into the Stat enum member, clamp line, wire
/// lines, PlayerStats field and modifier pipeline cases. The property
/// carries the type and default; this attribute carries the sim contract.
/// Presentation stays separate in [UiProperty]. statsName overrides the
/// derived name where the mechanical one is clumsy
/// (MortarReloadPerShell -> ReloadPerShell).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PlayerStatAttribute : Attribute
{
    // ConfigGenerator reads these from metadata, never through the getters.
    [UsedImplicitly] public float Min { get; }
    [UsedImplicitly] public float Max { get; }
    [UsedImplicitly] public StatConvert Convert { get; }
    [UsedImplicitly] public string? StatsName { get; }

    public PlayerStatAttribute(float min, float max,
        StatConvert convert = StatConvert.RAW, string? statsName = null)
    {
        Min = min;
        Max = max;
        Convert = convert;
        StatsName = statsName;
    }
}
