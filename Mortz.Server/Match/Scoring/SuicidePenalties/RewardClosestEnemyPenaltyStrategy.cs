using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring.SuicidePenalties;

public sealed class RewardClosestEnemyPenaltyStrategy : SuicidePenaltyStrategy
{
    public override KillReward? Apply(Player victim, Player? nearestEnemy, MatchScores scores)
    {
        if (nearestEnemy == null)
            return null;
        int kills = scores.AddKills(nearestEnemy, +1);
        return new KillReward(nearestEnemy.PeerId, kills);
    }
}
