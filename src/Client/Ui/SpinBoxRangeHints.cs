using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Missing hints keep the SpinBox's scene defaults.</summary>
public static class SpinBoxRangeHints
{
    public static void ApplyRangeHints(this SpinBox spinBox, IUiPropertyDescriptor descriptor)
    {
        if (descriptor.Min is float min)
        {
            spinBox.MinValue = min;
            spinBox.AllowLesser = false;
        }
        if (descriptor.Max is float max)
        {
            spinBox.MaxValue = max;
            spinBox.AllowGreater = false;
        }
        if (descriptor.Step is float step)
            spinBox.Step = step;
    }
}
