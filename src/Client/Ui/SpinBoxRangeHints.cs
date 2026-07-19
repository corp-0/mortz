using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Missing hints keep the SpinBox's scene defaults.</summary>
internal static class SpinBoxRangeHints
{
    public static void ApplyRangeHints(this SpinBox spinBox, IUiPropertyDescriptor descriptor)
    {
        if (descriptor.Min is { } min)
        {
            spinBox.MinValue = min;
            spinBox.AllowLesser = false;
        }
        if (descriptor.Max is { } max)
        {
            spinBox.MaxValue = max;
            spinBox.AllowGreater = false;
        }
        if (descriptor.Step is { } step)
            spinBox.Step = step;
    }
}
