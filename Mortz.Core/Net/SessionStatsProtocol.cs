using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

public static class SessionStatsProtocol
{
    static SessionStatsProtocol()
    {
        SessionWinsMsg.Received += OnWins;
    }

    public static event Action<IReadOnlyList<PeerWins>>? WinsReceived;

    public static void BroadcastWins(IReadOnlyList<PeerWins> wins)
    {
        ArgumentNullException.ThrowIfNull(wins);
        WinsMsg(wins).Broadcast();
    }

    public static void SendWinsTo(int peerId, IReadOnlyList<PeerWins> wins)
    {
        ArgumentNullException.ThrowIfNull(wins);
        WinsMsg(wins).SendTo(peerId);
    }

    private static SessionWinsMsg WinsMsg(IReadOnlyList<PeerWins> wins) => new(
        wins.Select(win => win.PeerId).ToArray(),
        wins.Select(win => win.Wins).ToArray());

    private static void OnWins(SessionWinsMsg message)
    {
        if (message.Wins.Length != message.PeerIds.Length)
            return;
        PeerWins[] wins = new PeerWins[message.PeerIds.Length];
        for (int i = 0; i < wins.Length; i++)
        {
            wins[i] = new PeerWins(message.PeerIds[i], message.Wins[i]);
        }
        WinsReceived?.Invoke(wins);
    }
}
