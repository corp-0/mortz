using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring.SuicidePenalties;

public sealed class KillNoNegativePenaltyStrategy : SuicidePenaltyStrategy
{
    public override KillReward? Apply(Player victim, Player? nearestEnemy, MatchScores scores)
    {
        if (scores.Of(victim).Kills > 0)
            scores.AddKills(victim, -1);
        return null;
    }
}
