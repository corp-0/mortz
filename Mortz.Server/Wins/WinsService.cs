using Mortz.Core.Net.Stats;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Serilog;

namespace Mortz.Server.Wins;

/// <summary>Session win tallies. A leaver's count dies with their state cell,
/// so nothing removes anything.</summary>
public sealed class WinsService(
    ServerStateKeys keys,
    Roster roster,
    IServerLink link,
    ILogger log) : IObservePlayers
{
    private readonly ServerStateKey<SessionWins> _wins = keys.Claim<SessionWins>();

    public void PlayerJoined(Player jipPlayer) =>
        link.Send(jipPlayer.PeerId, SessionStatsProtocol.Encode(Table()));

    public void PlayerLeft(Player player) { }

    public void Record(IReadOnlyList<Player> winners)
    {
        foreach (Player player in winners)
        {
            SessionWins wins = player.State(_wins);
            wins.Count++;
            log.Information("{PlayerName} now has {Wins} session win(s)", player.Name, wins.Count);
        }
        link.Broadcast(SessionStatsProtocol.Encode(Table()));
    }

    // Match end and join only, never per tick.
    private PeerWins[] Table()
    {
        PeerWins[] table = new PeerWins[roster.Count];
        int index = 0;
        foreach (Player player in roster)
        {
            table[index++] = new PeerWins(player.PeerId, player.State(_wins).Count);
        }
        return table;
    }
}
