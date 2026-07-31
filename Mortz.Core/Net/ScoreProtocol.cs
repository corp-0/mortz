using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

public static class ScoreProtocol
{
    static ScoreProtocol() => ScoreSyncMsg.Received += OnReceived;

    public static event Action<ScoreSync>? Received;

    public static void SendTo(int peerId, ScoreSync sync) => Encode(sync).SendTo(peerId);

    private static ScoreSyncMsg Encode(ScoreSync sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        return new ScoreSyncMsg(
            sync.Rows.Select(row => row.PeerId).ToArray(),
            sync.Rows.Select(row => row.Kills).ToArray(),
            sync.Rows.Select(row => row.Deaths).ToArray(),
            sync.TeamKills.Blue,
            sync.TeamKills.Red);
    }

    private static void OnReceived(ScoreSyncMsg message)
    {
        int count = message.PeerIds.Length;
        if (message.Kills.Length != count || message.Deaths.Length != count)
            return;
        ScoreRow[] rows = new ScoreRow[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = new ScoreRow(message.PeerIds[i], message.Kills[i], message.Deaths[i]);
        }
        Received?.Invoke(new ScoreSync(rows,
            new TeamKills(message.BlueKills, message.RedKills)));
    }
}
