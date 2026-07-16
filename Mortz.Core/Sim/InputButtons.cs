namespace Mortz.Core.Sim;

[Flags]
public enum InputButtons : ushort
{
    NONE = 0,
    LEFT = 1 << 0,
    RIGHT = 1 << 1,
    JUMP = 1 << 2,
    DASH = 1 << 3,
    ROPE = 1 << 4,
    UP = 1 << 5,
    DOWN = 1 << 6,
    FIRE = 1 << 7,
    RELOAD = 1 << 8,
    PARRY = 1 << 9,
}
