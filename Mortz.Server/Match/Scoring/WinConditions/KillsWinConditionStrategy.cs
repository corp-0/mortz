using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;

namespace Mortz.Server.Match.Scoring.WinConditions;

[VictoryRuleStrategy(typeof(KillsVictoryRules))]
public sealed class KillsWinConditionStrategy(KillsVictoryRules rules) : WinConditionStrategy
{
    public override Victor? Resolve(WinConditionContext context)
    {
        if (context.Rules.Teams)
        {
            foreach (Team team in Teams.All)
            {
                if (context.TeamKills[team] >= rules.Target)
                    return new Victor.Team(team);
            }
            return null;
        }

        foreach (SeatedScore row in context.Rows)
        {
            if (row.Score.Kills >= rules.Target)
                return new Victor.Player(row.Player.PeerId);
        }
        return null;
    }

    public override MatchStanding Standing(WinConditionContext context)
    {
        int best = 0;
        Victor? leader = null;
        if (context.Rules.Teams)
        {
            foreach (Team team in Teams.All)
            {
                if (context.TeamKills[team] <= best)
                    continue;
                best = context.TeamKills[team];
                leader = new Victor.Team(team);
            }
        }
        else
        {
            foreach (SeatedScore row in context.Rows)
            {
                if (row.Score.Kills <= best)
                    continue;
                best = row.Score.Kills;
                leader = new Victor.Player(row.Player.PeerId);
            }
        }

        return new MatchStanding(
            leader, Math.Max(0, rules.Target - best));
    }
}
