namespace Mortz.Core.Sim;

/// <summary>
/// One player's input for one simulation tick. Aim is the mouse direction
/// quantized to 256 steps (0 = +X/right, increasing clockwise on screen since
/// +Y is down); enough resolution for rope/weapon fire at minimal wire cost.
/// </summary>
public readonly record struct PlayerInput(InputButtons Buttons, byte Aim = 0)
{
    public bool Left => Buttons.HasFlag(InputButtons.LEFT);
    public bool Right => Buttons.HasFlag(InputButtons.RIGHT);
    public bool Jump => Buttons.HasFlag(InputButtons.JUMP);
    public bool Dash => Buttons.HasFlag(InputButtons.DASH);
    public bool Rope => Buttons.HasFlag(InputButtons.ROPE);
    public bool Up => Buttons.HasFlag(InputButtons.UP);
    public bool Down => Buttons.HasFlag(InputButtons.DOWN);
    public bool Fire => Buttons.HasFlag(InputButtons.FIRE);
    public bool Reload => Buttons.HasFlag(InputButtons.RELOAD);
    public bool Parry => Buttons.HasFlag(InputButtons.PARRY);

    /// <summary>-1, 0 or +1 horizontal drive.</summary>
    public int MoveDir => (Right ? 1 : 0) - (Left ? 1 : 0);

    /// <summary>8-way direction from the held movement keys; Zero when none held.</summary>
    public Vec2 HeldDir
    {
        get
        {
            Vec2 dir = new Vec2(MoveDir, (Down ? 1f : 0f) - (Up ? 1f : 0f));
            return dir == Vec2.Zero ? Vec2.Zero : dir.Normalized();
        }
    }

    public Vec2 AimDir => AimToDir(Aim);

    public static Vec2 AimToDir(byte aim)
    {
        float angle = aim * (MathF.Tau / 256f);
        return new Vec2(MathF.Cos(angle), MathF.Sin(angle));
    }

    public static byte AimFromVector(Vec2 dir)
    {
        float angle = MathF.Atan2(dir.Y, dir.X);
        if (angle < 0) angle += MathF.Tau;
        return (byte)(int)MathF.Round(angle / MathF.Tau * 256f % 256f);
    }
}
