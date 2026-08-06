namespace Mortz.Core.Net.Score;

/// <summary>Score seed for the match sync: full per-player table plus team
/// totals, so a late joiner's score isn't blank. Eliminations keep it current
/// after that.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ScoreSyncMsg(
    ScoreRow[] Rows,
    int BlueKills,
    int RedKills
);
