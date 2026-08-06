using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Scoring.WinConditions;

public abstract class WinConditionStrategy
{
    public static WinConditionStrategy Create(WinCondition condition) =>
        condition switch
        {
            WinCondition.KILLS => new KillsWinConditionStrategy(),
            WinCondition.KILL_LEAD => new KillLeadWinConditionStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition,
                "Unsupported win condition."),
        };

    public abstract Victor? Resolve(WinConditionContext context);

    public abstract MatchStanding Standing(WinConditionContext context);
}
