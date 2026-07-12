using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>Keyboard and mouse mapped to sim input bits.</summary>
public static class InputSampler
{
    public static InputButtons Sample()
    {
        InputButtons buttons = InputButtons.None;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))
            buttons |= InputButtons.Left;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right))
            buttons |= InputButtons.Right;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            buttons |= InputButtons.Up;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            buttons |= InputButtons.Down;
        if (Input.IsPhysicalKeyPressed(Key.Space))
            buttons |= InputButtons.Jump;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            buttons |= InputButtons.Dash;
        if (Input.IsMouseButtonPressed(MouseButton.Right))
            buttons |= InputButtons.Rope;
        if (Input.IsMouseButtonPressed(MouseButton.Left))
            buttons |= InputButtons.Fire;
        if (Input.IsPhysicalKeyPressed(Key.R))
            buttons |= InputButtons.Reload;
        return buttons;
    }
}
