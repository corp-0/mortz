namespace Mortz.Core.Net.Messages;

/// <summary>Score seed sent with the match sync: the full per-player table and
/// both team totals as they stand, so a late joiner's score display doesn't
/// start blank. The elimination stream keeps it current afterwards.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ScoreSyncMsg(
    long[] PeerIds, int[] Kills, int[] Deaths, int Team1Kills, int Team2Kills);
