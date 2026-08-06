using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring.SuicidePenalties;

public sealed class NoPenaltyStrategy : SuicidePenaltyStrategy
{
    public override KillReward? Apply(Player victim, Player? nearestEnemy, MatchScores scores) =>
        null;
}
