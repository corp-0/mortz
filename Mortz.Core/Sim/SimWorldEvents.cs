namespace Mortz.Core.Sim;

/// <summary>A death the sim resolved this Step</summary>
public readonly record struct Death(
    int PeerId,
    Vec2 Position,
    int KillerId,
    bool Owned,
    int ShellId);

public readonly record struct Explosion(int X, int Y, int Radius, int OwnerId, int SpawnSeq);

/// <summary>The server took over a predicted shell; sent reliably to the
/// original shooter so their ghost stops flying.</summary>
public readonly record struct ShellRetirement(int FiredBy, int SpawnSeq);
