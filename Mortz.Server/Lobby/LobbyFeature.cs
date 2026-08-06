using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Lobby;

/// <summary>Ready-up, team moves and swap offers.</summary>
public sealed class LobbyFeature(
    LobbySession session,
    IServerLink link,
    ILogger log,
    PhaseControl control)
    :
        IHandle<Player, SetReadyMsg>,
        IHandle<Player, TeamJoinRequestMsg>,
        IHandle<Player, TeamSwapRequestMsg>
{
    public void Handle(Player sender, in SetReadyMsg message)
    {
        if (!session.SetReady(sender, message.Ready))
            return;
        log.Information("player {PeerId} is {Readiness}", sender.PeerId,
            message.Ready ? "ready" : "not ready");
        BroadcastRoster();
        if (session.CanStart)
            control.Request(PhaseRequest.START_MATCH);
    }

    public void Handle(Player sender, in TeamJoinRequestMsg message)
    {
        if (TeamWire.FromByte(message.Team) is not Team team ||
            !session.TrySetTeam(sender, team))
        {
            return;
        }

        log.Information("player {PeerId} moved to {Team}", sender.PeerId, Teams.Name(team));
        BroadcastRoster();
    }

    public void Handle(Player sender, in TeamSwapRequestMsg message)
    {
        SwapResult result = session.RequestSwap(sender, message.TargetPeerId);
        if (result == SwapResult.NONE)
            return;
        switch (result)
        {
            case SwapResult.OFFERED:
                log.Information("player {PeerId} offers a team swap to {TargetPeerId}",
                    sender.PeerId, message.TargetPeerId);
                break;
            case SwapResult.CANCELLED:
                log.Information("player {PeerId} cancelled their swap offer", sender.PeerId);
                break;
            default:
                log.Information("players {PeerId} and {TargetPeerId} swapped teams",
                    sender.PeerId, message.TargetPeerId);
                break;
        }
        BroadcastRoster();
    }

    public void BroadcastRoster() =>
        link.Broadcast(new LobbyStateMsg(session.Players, session.SwapOffers));

    /// <summary>Follows the Teams rule after a settings change.</summary>
    public void ApplyTeamsRule(bool enabled)
    {
        if (!session.SetTeamsEnabled(enabled))
            return;
        log.Information("lobby teams {TeamsState}", enabled ? "assigned" : "cleared");
        BroadcastRoster();
    }
}
