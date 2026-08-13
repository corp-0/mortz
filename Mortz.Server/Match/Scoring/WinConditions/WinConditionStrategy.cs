using Mortz.Core.Match.Scoring;

namespace Mortz.Server.Match.Scoring.WinConditions;

public abstract partial class WinConditionStrategy
{
    public abstract Victor? Resolve(WinConditionContext context);

    public abstract MatchStanding Standing(WinConditionContext context);
}
