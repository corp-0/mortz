namespace Mortz.Core.Sim;

/// <summary>
/// Complete simulation state of one player. Prediction copies and re-ticks
/// these, so anything that affects gameplay has to live here, not in nodes.
/// It also has to be on the wire, or replaying from a server state drifts.
/// </summary>
public record struct PlayerState
{
    public int PeerId;
    /// <summary>Stable 1..MAX_PLAYERS wire id for this match; roster metadata, not gameplay state.</summary>
    public byte NetSlot;
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

    /// <summary>Ticks left on the active parry bubble; nonzero = deflecting shells.</summary>
    public byte ParryTicks;

    /// <summary>Ticks until the next parry. Charged in full at the press;
    /// SimWorld zeroes it when the bubble deflects a shell, so only a whiff
    /// pays. ushort: 20 s of ticks overflows a byte.</summary>
    public ushort ParryCooldown;

    /// <summary>Aim byte from the last applied input, so remote clients can render the weapon.</summary>
    public byte Aim;

    /// <summary>Server-authoritative and never predicted: blasts subtract it in
    /// SimWorld only, prediction just carries the acked value through replay.
    /// 0 while the body lies dead; respawn restores MAX_HEALTH.</summary>
    public byte Health;

    /// <summary>Ticks until respawn; nonzero = dead. A dead body is frozen and
    /// untouchable: PlayerSim and WeaponSim no-op on it (which also freezes
    /// prediction replay), blasts skip it, shells fly through it. Only the
    /// server counts it down and respawns at 0.</summary>
    public byte RespawnTicks;

    /// <summary>Ticks of spawn protection left; nonzero = can't shoot, can't be
    /// hurt. PlayerSim counts it down so prediction and the server agree on the
    /// first tick a shot is allowed. Clients flicker the body while it runs.</summary>
    public byte SpawnImmunityTicks;

    /// <summary>Last input seq that still counts as protected. The tick timer
    /// runs on sim time, so a click pressed while protected can reach the server
    /// after the timer expired and fire anyway; this fence catches those. Rides
    /// in the owner's full snapshot so replay agrees.</summary>
    public int SpawnImmunityFireThroughSeq;

    /// <summary>Sprite frame dealt by the server at join; survives respawns.</summary>
    public byte Skin;

    /// <summary>0 = no team, 1/2 = the teams. Assigned in the lobby, frozen for
    /// the match like every other rule; the sim reads it for friendly fire.</summary>
    public byte TeamId;

    /// <summary>
    /// Newest input sequence the server applied to this player (-1 before any).
    /// Set by SimWorld, not PlayerSim; this is the ack prediction replays from.
    /// Not in the snapshot itself: only the owner cares, so the server sends
    /// each client its own ack beside the packet.
    /// </summary>
    public int LastInputSeq;

    /// <summary>Authoritative raw buttons held on the newest consumed input, for
    /// press-edge detection (jump, dash, rope, weapon). Serialized because queue
    /// draining and respawn mean it cannot safely be inferred from the ack.</summary>
    public InputButtons PrevButtons;
}
