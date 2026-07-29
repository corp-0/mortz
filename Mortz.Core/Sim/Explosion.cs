namespace Mortz.Core.Sim;

/// <summary>A terrain impact. OwnerId and SpawnSeq let the owner's client
/// match its predicted carve to the authoritative one; SpawnSeq is -1 for a
/// deflected shell, whose carve matches no prediction.</summary>
public readonly record struct Explosion(int X, int Y, int Radius, int OwnerId, int SpawnSeq);
