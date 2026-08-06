using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;

namespace Mortz.Server.Match.Scoring.WinConditions;

public sealed class KillLeadWinConditionStrategy : WinConditionStrategy
{
    public override Victor? Resolve(WinConditionContext context)
    {
        MatchStanding standing = Standing(context);
        if (standing.Leader is not Victor leader || standing.Remaining != 0)
            return null;
        return leader;
    }

    public override MatchStanding Standing(WinConditionContext context) =>
        context.Rules.Teams ? TeamStanding(context) : PlayerStanding(context);

    private static MatchStanding TeamStanding(WinConditionContext context)
    {
        LeadRace race = new(context.Rules.KillLeadTarget);
        foreach (Team team in Teams.All)
        {
            race.Offer(new Victor.Team(team), context.TeamKills[team]);
        }
        return race.Standing();
    }

    private static MatchStanding PlayerStanding(WinConditionContext context)
    {
        LeadRace race = new(context.Rules.KillLeadTarget);
        foreach (SeatedScore row in context.Rows)
        {
            race.Offer(new Victor.Player(row.Player.PeerId), row.Score.Kills);
        }
        return race.Standing();
    }
}
