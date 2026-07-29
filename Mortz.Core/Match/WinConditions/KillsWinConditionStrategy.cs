namespace Mortz.Core.Match.WinConditions;

public sealed class KillsWinConditionStrategy : WinConditionStrategy
{
    public override Victor? Resolve(WinConditionContext context)
    {
        if (context.Rules.Teams)
        {
            foreach (Team team in Teams.ALL)
            {
                if (context.TeamKills[team] >= context.Rules.KillTarget)
                    return new TeamVictor(team);
            }
            return null;
        }

        foreach ((int peerId, Scoreboard.Row row) in context.Rows)
        {
            if (row.Kills >= context.Rules.KillTarget)
                return new PlayerVictor(peerId);
        }
        return null;
    }

    public override Scoreboard.MatchStanding Standing(WinConditionContext context)
    {
        int best = 0;
        Victor? leader = null;
        if (context.Rules.Teams)
        {
            foreach (Team team in Teams.ALL)
            {
                if (context.TeamKills[team] <= best)
                    continue;
                best = context.TeamKills[team];
                leader = new TeamVictor(team);
            }
        }
        else
        {
            foreach ((int peerId, Scoreboard.Row row) in context.Rows)
            {
                if (row.Kills <= best)
                    continue;
                best = row.Kills;
                leader = new PlayerVictor(peerId);
            }
        }

        return new Scoreboard.MatchStanding(
            leader, Math.Max(0, context.Rules.KillTarget - best));
    }
}
