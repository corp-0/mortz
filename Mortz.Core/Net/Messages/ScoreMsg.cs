namespace Mortz.Core.Net.Messages;

/// <summary>One scored death with the authoritative tallies after it; the
/// stream doubles as the kill feed. KillerId 0 (death pit) or equal to
/// VictimId is a suicide, and KillerKills then carries the victim's own
/// (possibly penalized) kill count.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ScoreMsg(
    long KillerId, long VictimId, int KillerKills, int VictimDeaths, int Team1Kills, int Team2Kills);
