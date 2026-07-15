namespace Mortz.Core.Net.Messages;

[Flags]
public enum FinalKillFlags : byte
{
    NONE = 0,
    EXPLOSION = 1 << 0,
    FALL = 1 << 1,
    SUICIDE = 1 << 2,
    TEAM_KILL = 1 << 3,
    OWNED = 1 << 4,
}

/// <summary>The authoritative event that decided the match. The simulation
/// tick anchors the client's render history; death and optional impact points
/// let presentation replay non-mortar deaths without inventing a carve.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct FinalKillMsg(
    int Tick,
    int KillerId,
    int VictimId,
    FinalKillFlags Flags,
    short DeathX,
    short DeathY,
    short ImpactX,
    short ImpactY,
    byte BlastRadius);
