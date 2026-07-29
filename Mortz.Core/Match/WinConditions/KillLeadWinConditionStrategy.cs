namespace Mortz.Core.Match.WinConditions;

public sealed class KillLeadWinConditionStrategy : WinConditionStrategy
{
    public override Victor? Resolve(WinConditionContext context)
    {
        Scoreboard.MatchStanding standing = Standing(context);
        if (standing.Leader is not Victor leader || standing.Remaining != 0)
            return null;
        return leader;
    }

    public override Scoreboard.MatchStanding Standing(WinConditionContext context) =>
        context.Rules.Teams ? TeamStanding(context) : PlayerStanding(context);

    private static Scoreboard.MatchStanding TeamStanding(WinConditionContext context)
    {
        LeadRace race = new(context.Rules.KillLeadTarget);
        foreach (Team team in Teams.ALL)
        {
            race.Offer(new TeamVictor(team), context.TeamKills[team]);
        }
        return race.Standing();
    }

    private static Scoreboard.MatchStanding PlayerStanding(WinConditionContext context)
    {
        LeadRace race = new(context.Rules.KillLeadTarget);
        foreach ((int peerId, Scoreboard.Row row) in context.Rows)
        {
            race.Offer(new PlayerVictor(peerId), row.Kills);
        }
        return race.Standing();
    }
}
