using Mortz.Core.Match.Scoring;
using Mortz.Core.Net.Match;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Serilog;

namespace Mortz.Server.Match.Services;

/// <summary>Publishes the match-point presentation state and keeps it available for late joiners.</summary>
public class MatchPointService(IServerLink link, ILogger log) : IObserveMatchUpdate, IEnterMatch, ISyncJip
{
    private const int MATCH_POINT_REMAINING = 1;

    public MatchPoint? Active { get; private set; }

    public void MatchUpdated(in MatchUpdate update, ServerTime time)
    {
        bool active = update.MatchEnded == null &&
                      update.Standing.Remaining == MATCH_POINT_REMAINING;
        if (active == (Active != null))
            return;

        Active = active
            ? new MatchPoint(update.Standing.Remaining, update.Standing.Leader)
            : null;
        log.Information("match point {MatchPointState}", Active != null ? "on" : "off");
        link.Broadcast(MatchProtocol.Encode(Active));
    }

    public void Enter(Player player, int generation, bool initialPhase)
    {
        if (initialPhase)
            SendCurrent(player);
    }

    public void Sync(Player jipPlayer) => SendCurrent(jipPlayer);

    private void SendCurrent(Player player)
    {
        if (Active != null)
            link.Send(player.PeerId, MatchProtocol.Encode(Active));
    }
}
