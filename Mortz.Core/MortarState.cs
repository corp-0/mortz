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
    /// <summary>PeerId of the shooter. On the wire: the owner's client hides
    /// the authoritative copy and renders its own predicted shell instead.</summary>
    public int OwnerId;

    /// <summary>Input sequence that fired the shell. Not in snapshots; it rides
    /// the carve broadcast so the owner's client can match the authoritative
    /// explosion to its predicted one.</summary>
    public int SpawnSeq;
    public Vec2 Position;
    public Vec2 Velocity;
}
