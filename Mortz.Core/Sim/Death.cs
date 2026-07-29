namespace Mortz.Core.Sim;

/// <summary>A death the sim resolved this Step, body center at the moment of
/// death. KillerId is the explosion's owner (the parrier for a parried
/// shell), 0 for a death pit, the victim's own id for suicide. Owned = a
/// parried shell killed the very player who fired it. ShellId is the killing
/// shell's mortar id, -1 when no shell was involved.</summary>
public readonly record struct Death(
    int PeerId,
    Vec2 Position,
    int KillerId,
    bool Owned,
    int ShellId);
