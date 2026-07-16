using Godot;
using Mortz.Client.Chat;
using Mortz.Core.Sim;

namespace Mortz.Client.Match;

/// <summary>Input actions (see project.godot) mapped to sim input bits.</summary>
public static class InputSampler
{
    // Cached StringNames: Sample runs every sim tick, no per-call conversions.
    private static readonly (StringName Action, InputButtons Button)[] _bindings =
    [
        ("move_left", InputButtons.LEFT),
        ("move_right", InputButtons.RIGHT),
        ("move_up", InputButtons.UP),
        ("move_down", InputButtons.DOWN),
        ("jump", InputButtons.JUMP),
        ("dash", InputButtons.DASH),
        ("rope", InputButtons.ROPE),
        ("fire", InputButtons.FIRE),
        ("reload", InputButtons.RELOAD),
        ("parry", InputButtons.PARRY),
    ];

    public static InputButtons Sample()
    {
        if (ChatInputGuard.IsTyping)
            return InputButtons.NONE;
        InputButtons buttons = InputButtons.NONE;
        foreach ((StringName action, InputButtons button) in _bindings)
        {
            if (Input.IsActionPressed(action))
                buttons |= button;
        }
        return buttons;
    }
}
