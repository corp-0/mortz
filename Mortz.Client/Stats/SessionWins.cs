using Mortz.Client.Players;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's wins this session as last told by the server.</summary>
public sealed class WinCount
{
    public int Value { get; set; }
}

/// <summary>Applies the session-wins stream onto players. Each broadcast is a
/// full table, so absent players reset to zero.</summary>
public class SessionWins(ClientPlayers players) : IHandle<SessionWinsMsg>
{
    private readonly SessionStateKey<WinCount> _wins = players.SessionKeys.Claim<WinCount>(typeof(SessionWins));

    public event Action? Changed;

    public int Of(ClientPlayer player) => player.State(_wins).Value;

    public void Handle(in SessionWinsMsg message)
    {
        if (!SessionStatsProtocol.TryDecode(message, out PeerWins[]? wins))
            return;
        foreach (ClientPlayer player in players)
        {
            player.State(_wins).Value = 0;
        }
        foreach (PeerWins win in wins)
        {
            players.GetOrCreate(win.PeerId).State(_wins).Value = win.Wins;
        }
        Changed?.Invoke();
    }
}
