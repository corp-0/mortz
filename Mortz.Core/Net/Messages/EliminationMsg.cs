namespace Mortz.Core.Net.Messages;

[Flags]
public enum EliminationFlags : byte
{
    NONE = 0,
    SUICIDE = 1 << 0,
    FALL = 1 << 1,
    TEAM_KILL = 1 << 2,
    OWNED = 1 << 3,
    FIRST_BLOOD = 1 << 4,
}

/// <summary>One scored death with the authoritative tallies after it; the
/// stream doubles as the kill feed. On a suicide KillerKills carries the
/// victim's own (possibly penalized) kill count.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct EliminationMsg(
    long KillerId,
    long VictimId,
    EliminationFlags Flags,
    int KillerKills,
    int VictimDeaths,
    int Team1Kills,
    int Team2Kills);
