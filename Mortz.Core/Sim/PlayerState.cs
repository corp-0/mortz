namespace Mortz.Core.Sim;

/// <summary>
/// Complete sim state of one player. Anything that affects gameplay must
/// live here and ride the wire, or prediction replay drifts.
/// </summary>
public record struct PlayerState
{
    public int PeerId;
    /// <summary>Stable 1..MAX_PLAYERS wire id for this match; roster metadata, not gameplay state.</summary>
    public byte NetSlot;
    public Vec2 Position;
    public Vec2 Velocity;
    public bool Grounded;

    /// <summary>Jump presses remaining; refilled while grounded.</summary>
    public byte JumpsLeft;

    /// <summary>Ticks until the next dash is allowed.</summary>
    public byte DashCooldown;

    /// <summary>Mortar shells in the magazine.</summary>
    public byte Ammo;

    /// <summary>Ticks until the next shell banks; 0 = not reloading.</summary>
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

    /// <summary>Ticks left on the active parry bubble; nonzero = deflecting shells.</summary>
    public byte ParryTicks;

    /// <summary>Ticks until the next parry; charged at the press, zeroed on a
    /// deflect. ushort: 20 s of ticks overflows a byte.</summary>
    public ushort ParryCooldown;

    /// <summary>Aim byte from the last applied input, so remote clients can render the weapon.</summary>
    public byte Aim;

    /// <summary>Server-authoritative, never predicted: prediction carries the
    /// acked value through replay.</summary>
    public byte Health;

    /// <summary>Ticks until respawn; nonzero = dead. A dead body is frozen:
    /// PlayerSim and WeaponSim no-op on it, blasts skip it, shells fly
    /// through. Only the server counts it down.</summary>
    public byte RespawnTicks;

    /// <summary>Ticks of spawn protection; nonzero = can't shoot, can't be
    /// hurt. PlayerSim counts it down so prediction and server agree on the
    /// first tick a shot is allowed.</summary>
    public byte SpawnImmunityTicks;

    /// <summary>Last input seq that still counts as protected. A click pressed
    /// while protected can reach the server after the tick timer expired and
    /// fire anyway; this fence catches those. Rides the owner's snapshot so
    /// replay agrees.</summary>
    public int SpawnImmunityFireThroughSeq;

    /// <summary>Sprite frame dealt by the server at join; survives respawns.</summary>
    public byte Skin;

    /// <summary>0 = no team, 1/2 = the teams; the sim reads it for friendly fire.</summary>
    public byte TeamId;

    /// <summary>Newest input seq the server applied (-1 before any); the ack
    /// prediction replays from. Not in the snapshot: the server sends each
    /// client its own ack beside the packet.</summary>
    public int LastInputSeq;

    /// <summary>Raw buttons held on the newest consumed input, for press-edge
    /// detection. Serialized because queue draining and respawn make it
    /// non-inferable from the ack.</summary>
    public InputButtons PrevButtons;
}
