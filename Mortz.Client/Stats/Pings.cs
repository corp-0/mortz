using Mortz.Client.Players;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's latest ping, null until an update mentions them.</summary>
public sealed class PingSample
{
    public int? Ms { get; set; }
}

/// <summary>Applies the ping stream onto players. Each update is a full table,
/// so absent players reset to no sample.</summary>
public class Pings(ClientPlayers players) : IHandle<PingUpdateMsg>
{
    private readonly SessionStateKey<PingSample> _ping = players.SessionKeys.Claim<PingSample>(typeof(Pings));

    public event Action? Changed;

    public int? Of(ClientPlayer player) => player.State(_ping).Ms;

    public void Handle(in PingUpdateMsg message)
    {
        foreach (ClientPlayer player in players)
        {
            player.State(_ping).Ms = null;
        }
        foreach (PeerPing ping in message.Pings)
        {
            players.GetOrCreate(ping.PeerId).State(_ping).Ms = ping.PingMs;
        }
        Changed?.Invoke();
    }
}
