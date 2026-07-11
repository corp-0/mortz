namespace Mortz.Core;

[Flags]
public enum InputButtons : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Jump = 1 << 2,
    Dash = 1 << 3,
    Rope = 1 << 4,
    Up = 1 << 5,
    Down = 1 << 6,
}

/// <summary>
/// One player's input for one simulation tick. Aim is the mouse direction
/// quantized to 256 steps (0 = +X/right, increasing clockwise on screen since
/// +Y is down); enough resolution for rope/weapon fire at minimal wire cost.
/// </summary>
public readonly record struct PlayerInput(InputButtons Buttons, byte Aim = 0)
{
    public bool Left => (Buttons & InputButtons.Left) != 0;
    public bool Right => (Buttons & InputButtons.Right) != 0;
    public bool Jump => (Buttons & InputButtons.Jump) != 0;
    public bool Dash => (Buttons & InputButtons.Dash) != 0;
    public bool Rope => (Buttons & InputButtons.Rope) != 0;
    public bool Up => (Buttons & InputButtons.Up) != 0;
    public bool Down => (Buttons & InputButtons.Down) != 0;

    /// <summary>-1, 0 or +1 horizontal drive.</summary>
    public float MoveDir => (Right ? 1f : 0f) - (Left ? 1f : 0f);

    /// <summary>8-way direction from the held movement keys; Zero when none held.</summary>
    public Vec2 HeldDir
    {
        get
        {
            Vec2 dir = new Vec2(MoveDir, (Down ? 1f : 0f) - (Up ? 1f : 0f));
            return dir == Vec2.Zero ? Vec2.Zero : dir.Normalized();
        }
    }

    public Vec2 AimDir
    {
        get
        {
            float angle = Aim * (MathF.Tau / 256f);
            return new Vec2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }

    public static byte AimFromVector(Vec2 dir)
    {
        float angle = MathF.Atan2(dir.Y, dir.X);
        if (angle < 0) angle += MathF.Tau;
        return (byte)(int)MathF.Round(angle / MathF.Tau * 256f % 256f);
    }
}
