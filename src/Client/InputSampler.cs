using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>Input actions (see project.godot) mapped to sim input bits.</summary>
public static class InputSampler
{
    // Cached StringNames: Sample runs every sim tick, no per-call conversions.
    private static readonly (StringName Action, InputButtons Button)[] _bindings =
    [
        ("move_left", InputButtons.Left),
        ("move_right", InputButtons.Right),
        ("move_up", InputButtons.Up),
        ("move_down", InputButtons.Down),
        ("jump", InputButtons.Jump),
        ("dash", InputButtons.Dash),
        ("rope", InputButtons.Rope),
        ("fire", InputButtons.Fire),
        ("reload", InputButtons.Reload),
        ("parry", InputButtons.Parry),
    ];

    public static InputButtons Sample()
    {
        InputButtons buttons = InputButtons.None;
        foreach ((StringName action, InputButtons button) in _bindings)
        {
            if (Input.IsActionPressed(action))
                buttons |= button;
        }
        return buttons;
    }
}
