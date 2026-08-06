using JetBrains.Annotations;

namespace Mortz.Core.Match.Configuration;

/// <summary>
/// Declares a MatchConfig property as a match-level rule (teams, win
/// condition, mortar ballistics): joins the generated clamp and wire, but
/// gets no Stat enum member or PlayerStats field: rules are not
/// modifier-targetable. min/max apply to numeric rules only; the NaN
/// default means unclamped (bool and enum rules). Presentation stays
/// separate in [UiProperty]/[UiCategory].
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MatchRuleAttribute(float min = float.NaN, float max = float.NaN) : Attribute
{
    // ConfigGenerator reads these from metadata, never through the getters.
    [UsedImplicitly] public float Min { get; } = min;
    [UsedImplicitly] public float Max { get; } = max;
}
