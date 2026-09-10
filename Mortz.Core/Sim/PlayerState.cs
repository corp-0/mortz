using Mortz.Core.Match.Teams;

namespace Mortz.Core.Sim;

/// <summary>Replicated body state used by simulation and prediction.</summary>
public record struct PlayerState
{
    public int PeerId;
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

    /// <summary>Authoritative countdown; zero on a dead player means no scheduled return.</summary>
    public ushort RespawnTicks;

    /// <summary>Ticks of spawn protection; nonzero = can't shoot, can't be
    /// hurt. PlayerSim counts it down so prediction and server agree on the
    /// first tick a shot is allowed.</summary>
    public byte SpawnImmunityTicks;

    /// <summary>Last input seq that still counts as protected. A click pressed
    /// while protected can reach the server after the tick timer expired and
    /// fire anyway; this fence catches those. Rides the owner's snapshot so
    /// replay agrees.</summary>
    public int SpawnImmunityFireThroughSeq;


    /// <summary>Null when they have no team; the sim reads it for friendly
    /// fire.</summary>
    public Team? Team;

    /// <summary>Newest input seq the server applied (-1 before any); the ack
    /// prediction replays from. Not in the snapshot: the server sends each
    /// client its own ack beside the packet.</summary>
    public int LastInputSeq;

    /// <summary>Raw buttons held on the newest consumed input, for press-edge
    /// detection. Serialized because queue draining and respawn make it
    /// non-inferable from the ack.</summary>
    public InputButtons PrevButtons;

    public readonly bool IsAlive => Health > 0;

    public readonly bool IsAtCriticalHealth(byte maxHealth) =>
        IsAlive && Health * 3 <= maxHealth;

    public readonly Vec2 BodyCenter =>
        Position with { Y = Position.Y - SimConfig.PLAYER_HALF_HEIGHT };
}
