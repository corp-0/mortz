using Mortz.Core.Net.Lobby;

namespace Mortz.Server.Lobby;

/// <summary>Publishes immutable lobby snapshots to clients.</summary>
public sealed class LobbyReplication(IServerLink link)
{
    public void Publish(LobbyUpdate update) =>
        link.Broadcast(new LobbyStateMsg(
            update.Snapshot.Members.ToArray(),
            update.Snapshot.Offers.ToArray()));
}
