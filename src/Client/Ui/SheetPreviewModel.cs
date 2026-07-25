using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Editor preview only, never used at runtime.</summary>
public sealed class SheetPreviewModel
{
    public enum Flavor
    {
        QUICK_MATCH,
        ENDLESS_SIEGE,
    }

    [UiCategory("Preview Category")]
    [UiProperty("Bool Rule")]
    public bool BoolRule { get; set; } = true;

    [UiProperty("Int Rule", min: 1, max: 99)]
    public int IntRule { get; set; } = 25;

    [UiCategory("Another Category")]
    [UiProperty("Float Rule", min: 0, max: 10, step: 0.5f)]
    public float FloatRule { get; set; } = 2.5f;

    [UiProperty("Enum Rule")]
    public Flavor EnumRule { get; set; }
}
