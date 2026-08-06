using System.Diagnostics.CodeAnalysis;

namespace Mortz.Core.Net.Stats;

public static class SessionStatsProtocol
{
    public static SessionWinsMsg Encode(IReadOnlyList<PeerWins> wins)
    {
        ArgumentNullException.ThrowIfNull(wins);
        return new SessionWinsMsg(
            wins.Select(win => win.PeerId).ToArray(),
            wins.Select(win => win.Wins).ToArray());
    }

    public static bool TryDecode(
        SessionWinsMsg message,
        [NotNullWhen(true)] out PeerWins[]? wins)
    {
        wins = null;
        if (message.Wins.Length != message.PeerIds.Length)
            return false;
        wins = new PeerWins[message.PeerIds.Length];
        for (int i = 0; i < wins.Length; i++)
        {
            wins[i] = new PeerWins(message.PeerIds[i], message.Wins[i]);
        }
        return true;
    }
}
