namespace Mortz.Core.Sim;

public static class InputButtonsExtensions
{
    public static InputButtons Only(this InputButtons buttons, InputButtons mask) =>
        buttons & mask;

    public static InputButtons Except(this InputButtons buttons, InputButtons other) =>
        buttons & ~other;
}
