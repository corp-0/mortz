using System.Diagnostics.CodeAnalysis;

namespace Mortz.Core.Net.Stats;
/// <summary>
/// Record of wins of this player on current lobby
/// </summary>
[NetRow]
public readonly partial record struct PeerWins(int PeerId, int Wins);

/// <summary>Per-player wins sent on join and after each match win.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct SessionWinsMsg(PeerWins[] Rows);

public static class SessionStatsProtocol
{
    public static SessionWinsMsg Encode(IReadOnlyList<PeerWins> wins)
    {
        ArgumentNullException.ThrowIfNull(wins);
        return new SessionWinsMsg([.. wins]);
    }

    public static bool TryDecode(
        SessionWinsMsg message,
        [NotNullWhen(true)] out PeerWins[]? wins)
    {
        wins = message.Rows;
        if (wins == null)
        {
            return false;
        }
        return true;
    }
}
