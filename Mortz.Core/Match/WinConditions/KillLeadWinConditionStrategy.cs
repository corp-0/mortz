namespace Mortz.Core.Match.WinConditions;

public sealed class KillLeadWinConditionStrategy : WinConditionStrategy
{
    public override Scoreboard.MatchWinner? Resolve(WinConditionContext context)
    {
        Scoreboard.MatchStanding standing = Standing(context);
        if (standing.LeaderId == 0 || standing.Remaining != 0)
            return null;
        return new Scoreboard.MatchWinner(standing.LeaderIsTeam, standing.LeaderId);
    }

    public override Scoreboard.MatchStanding Standing(WinConditionContext context) =>
        context.Rules.Teams ? TeamStanding(context) : PlayerStanding(context);

    private static Scoreboard.MatchStanding TeamStanding(WinConditionContext context)
    {
        int best = int.MinValue;
        int second = int.MinValue;
        int leader = 0;
        bool tied = false;

        for (byte team = 1; team <= context.TeamCount; team++)
        {
            int kills = context.TeamKills(team);
            if (kills > best)
            {
                second = best;
                best = kills;
                leader = team;
                tied = false;
            }
            else if (kills == best)
            {
                second = best;
                tied = true;
            }
            else if (kills > second)
            {
                second = kills;
            }
        }

        return BuildStanding(
            leader, tied, best, second, context.TeamCount, true, context.Rules.KillLeadTarget);
    }

    private static Scoreboard.MatchStanding PlayerStanding(WinConditionContext context)
    {
        int best = int.MinValue;
        int second = int.MinValue;
        int leader = 0;
        bool tied = false;

        foreach ((int peerId, Scoreboard.Row row) in context.Rows)
        {
            if (row.Kills > best)
            {
                second = best;
                best = row.Kills;
                leader = peerId;
                tied = false;
            }
            else if (row.Kills == best)
            {
                second = best;
                tied = true;
            }
            else if (row.Kills > second)
            {
                second = row.Kills;
            }
        }

        return BuildStanding(
            leader, tied, best, second, context.Rows.Count, false,
            context.Rules.KillLeadTarget);
    }

    private static Scoreboard.MatchStanding BuildStanding(
        int leader,
        bool tied,
        int best,
        int second,
        int competitorCount,
        bool byTeam,
        int target)
    {
        if (competitorCount < 2 || tied)
            return new Scoreboard.MatchStanding(0, byTeam, target);

        long lead = (long)best - second;
        int remaining = (int)Math.Max(0, target - lead);
        return new Scoreboard.MatchStanding(leader, byTeam, remaining);
    }
}
