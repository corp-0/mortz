namespace Mortz.Core.Net.Score;

[NetRow]
public readonly partial record struct ScoreRow(int PeerId, int Kills, int Deaths);

/// <summary>Full player and team scores for initial sync; eliminations update them afterward.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ScoreSyncMsg(
    ScoreRow[] Rows,
    int BlueKills,
    int RedKills
);
