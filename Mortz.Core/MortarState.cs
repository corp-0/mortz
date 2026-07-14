namespace Mortz.Core;

/// <summary>
/// One mortar shell in flight. Server-authoritative only: clients render these
/// straight from snapshots, no prediction. Ids wrap; they only have to be
/// unique among shells alive at the same time so clients can match them
/// across snapshots for interpolation.
/// </summary>
public record struct MortarState
{
    public ushort Id;
    /// <summary>PeerId credited with whatever the shell does; a parry hands it
    /// to the parrier. On the wire: the owner's client hides the authoritative
    /// copy and renders its own predicted shell instead, unless Deflected.</summary>
    public int OwnerId;

    /// <summary>PeerId that fired the shell, set at spawn and never transferred.
    /// Serialized so a deflected shell's original owner can distinguish its
    /// client-local SpawnSeq from another player's identical sequence number.</summary>
    public int FiredBy;

    /// <summary>Set by a parry. On the wire: nobody predicted this trajectory,
    /// so even the new owner renders the authoritative copy.</summary>
    public bool Deflected;

    /// <summary>Input sequence that fired the shell. Rides the carve broadcast and
    /// the snapshot so the owner can match its predicted carve and spot a shell a
    /// parry took over.</summary>
    public int SpawnSeq;
    public Vec2 Position;
    public Vec2 Velocity;
}
