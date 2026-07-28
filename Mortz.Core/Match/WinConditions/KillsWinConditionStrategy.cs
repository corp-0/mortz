namespace Mortz.Core.Match.WinConditions;

public sealed class KillsWinConditionStrategy : WinConditionStrategy
{
    public override Scoreboard.MatchWinner? Resolve(WinConditionContext context)
    {
        if (context.Rules.Teams)
        {
            for (byte team = 1; team <= context.TeamCount; team++)
            {
                if (context.TeamKills(team) >= context.Rules.KillTarget)
                    return new Scoreboard.MatchWinner(true, team);
            }
            return null;
        }

        foreach ((int peerId, Scoreboard.Row row) in context.Rows)
        {
            if (row.Kills >= context.Rules.KillTarget)
                return new Scoreboard.MatchWinner(false, peerId);
        }
        return null;
    }

    public override Scoreboard.MatchStanding Standing(WinConditionContext context)
    {
        int best = 0;
        int leader = 0;
        bool byTeam = context.Rules.Teams;
        if (byTeam)
        {
            for (byte team = 1; team <= context.TeamCount; team++)
            {
                if (context.TeamKills(team) <= best)
                    continue;
                best = context.TeamKills(team);
                leader = team;
            }
        }
        else
        {
            foreach ((int peerId, Scoreboard.Row row) in context.Rows)
            {
                if (row.Kills <= best)
                    continue;
                best = row.Kills;
                leader = peerId;
            }
        }

        return new Scoreboard.MatchStanding(
            leader, byTeam, Math.Max(0, context.Rules.KillTarget - best));
    }
}
