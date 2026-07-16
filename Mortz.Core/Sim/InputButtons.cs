namespace Mortz.Core.Sim;

[Flags]
public enum InputButtons : ushort
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Jump = 1 << 2,
    Dash = 1 << 3,
    Rope = 1 << 4,
    Up = 1 << 5,
    Down = 1 << 6,
    Fire = 1 << 7,
    Reload = 1 << 8,
    Parry = 1 << 9,
}
