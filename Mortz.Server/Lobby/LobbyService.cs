using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Server.Players;

namespace Mortz.Server.Lobby;

/// <summary>Decodes lobby requests and forwards them to the lobby runtime.</summary>
public sealed class LobbyService(
    LobbySession session,
    Action<LobbyUpdate?> apply)
    :
        IHandle<Player, SetReadyMsg>,
        IHandle<Player, TeamJoinRequestMsg>,
        IHandle<Player, TeamSwapRequestMsg>
{
    public void Handle(Player sender, in SetReadyMsg message) =>
        apply(session.SetReady(sender, message.Ready));

    public void Handle(Player sender, in TeamJoinRequestMsg message)
    {
        if (TeamWire.FromByte(message.Team) is Team team)
            apply(session.TrySetTeam(sender, team));
    }

    public void Handle(Player sender, in TeamSwapRequestMsg message) =>
        apply(session.RequestSwap(sender, message.TargetPeerId));
}
