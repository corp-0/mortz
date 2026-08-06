using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring.SuicidePenalties;

public abstract class SuicidePenaltyStrategy
{
    public static SuicidePenaltyStrategy Create(SuicidePenalty penalty) =>
        penalty switch
        {
            SuicidePenalty.NONE => new NoPenaltyStrategy(),
            SuicidePenalty.KILL_NO_NEGATIVE => new KillNoNegativePenaltyStrategy(),
            SuicidePenalty.KILL => new KillPenaltyStrategy(),
            SuicidePenalty.REWARD_CLOSEST_ENEMY => new RewardClosestEnemyPenaltyStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(penalty), penalty,
                "Unsupported suicide penalty."),
        };

    public abstract KillReward? Apply(Player victim, Player? nearestEnemy, MatchScores scores);
}
