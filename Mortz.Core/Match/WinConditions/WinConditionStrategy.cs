namespace Mortz.Core.Match.WinConditions;

/// <summary>Owns winner and match-standing resolution for one win condition.</summary>
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

    public abstract Scoreboard.MatchStanding Standing(WinConditionContext context);
}
