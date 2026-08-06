using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring.SuicidePenalties;

public sealed class KillPenaltyStrategy : SuicidePenaltyStrategy
{
    public override KillReward? Apply(Player victim, Player? nearestEnemy, MatchScores scores)
    {
        scores.AddKills(victim, -1);
        return null;
    }
}
