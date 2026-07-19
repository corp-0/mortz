using JetBrains.Annotations;

namespace Mortz.Core.Ui;

/// <summary>Marks a property as renderable in any UI. min/max/step bound the
/// numeric control; NaN = keep the control's default. No fallback to the
/// [PlayerStat]/[MatchRule] range, state it here too.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiPropertyAttribute(
    string displayName,
    float min = float.NaN,
    float max = float.NaN,
    float step = float.NaN) : Attribute
{
    public string DisplayName { get; } = displayName;

    // UiMetadataGenerator reads these from metadata, never through the getters.
    [UsedImplicitly] public float Min { get; } = min;
    [UsedImplicitly] public float Max { get; } = max;
    [UsedImplicitly] public float Step { get; } = step;
}
