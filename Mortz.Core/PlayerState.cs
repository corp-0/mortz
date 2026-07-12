namespace Mortz.Core;

public enum RopeMode : byte
{
    None = 0,
    Flying = 1,
    Attached = 2,
}

/// <summary>
/// Complete simulation state of one player. Prediction copies and re-ticks
/// these, so anything that affects gameplay has to live here, not in nodes.
/// It also has to be on the wire (except PrevButtons, which comes back from
/// input history), or replaying from a server state drifts.
/// </summary>
public record struct PlayerState
{
    public int PeerId;
    public Vec2 Position;
    public Vec2 Velocity;
    public bool Grounded;

    /// <summary>
    /// Jump presses remaining from the 2-jump budget. Refilled to TOTAL_JUMPS
    /// while grounded; a ground/coyote/wall jump spends the first, air jumps
    /// spend the rest. Falling without jumping keeps the whole budget.
    /// </summary>
    public byte JumpsLeft;

    /// <summary>Ticks until the next dash is allowed.</summary>
    public byte DashCooldown;

    /// <summary>Mortar shells in the magazine. Weapon state lives in
    /// WeaponSim (run by both SimWorld and the Predictor, never PlayerSim)
    /// and rides the snapshot like everything else replayed.</summary>
    public byte Ammo;

    /// <summary>Ticks until the next shell loads; 0 = not reloading. Shells
    /// bank one at a time until the magazine is full; firing throws away the
    /// one in progress and stops the reload.</summary>
    public byte ReloadTicks;

    /// <summary>Grace ticks after leaving a ledge where a jump still counts as grounded.</summary>
    public byte CoyoteTicks;

    public RopeMode Rope;
    /// <summary>Ticks until the rope may fire again; misses cost more than releases.</summary>
    public byte RopeCooldown;
    /// <summary>Hook position: the anchor when attached, the projectile when flying.</summary>
    public Vec2 RopePoint;
    /// <summary>Hook velocity while flying.</summary>
    public Vec2 RopeVelocity;
    /// <summary>Slack threshold set at attach; the pull only acts at full stretch.</summary>
    public float RopeLength;

    /// <summary>Aim byte from the last applied input, so remote clients can render the weapon.</summary>
    public byte Aim;

    /// <summary>Sprite frame dealt by the server at join; survives respawns.</summary>
    public byte Skin;

    /// <summary>
    /// Newest input sequence the server applied to this player (-1 before any).
    /// Set by SimWorld, not PlayerSim; this is the ack prediction replays from.
    /// Not in the snapshot itself: only the owner cares, so the server sends
    /// each client its own ack beside the packet.
    /// </summary>
    public int LastInputSeq;

    /// <summary>Buttons held last tick, for press-edge detection (jump, dash, rope).</summary>
    public InputButtons PrevButtons;
}
