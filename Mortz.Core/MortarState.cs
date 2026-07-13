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
    /// Not in snapshots; the server compares it to a kill's victim to spot the
    /// OWNED case (a parried shell coming back for its own shooter).</summary>
    public int FiredBy;

    /// <summary>Set by a parry. On the wire: nobody predicted this trajectory,
    /// so even the new owner renders the authoritative copy.</summary>
    public bool Deflected;

    /// <summary>Input sequence that fired the shell. Not in snapshots; it rides
    /// the carve broadcast so the owner's client can match the authoritative
    /// explosion to its predicted one.</summary>
    public int SpawnSeq;
    public Vec2 Position;
    public Vec2 Velocity;
}
