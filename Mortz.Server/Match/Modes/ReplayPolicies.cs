using Mortz.Core.Match.Configuration;

namespace Mortz.Server.Match.Modes;

public interface IReplayPolicy
{
    FinalKillEvent? Select(GameModeContext context, ModeResolution resolution, ScoredElimination cause);
}

public static class ReplayPolicies
{
    public static IReplayPolicy Create(ReplayRule rule) => rule switch
    {
        ReplayRule.NONE => new NoReplay(),
        ReplayRule.DECISIVE_ELIMINATION => new DecisiveEliminationReplay(),
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };
}

public class NoReplay : IReplayPolicy
{
    public FinalKillEvent? Select(GameModeContext context, ModeResolution resolution, ScoredElimination cause) => null;
}

public class DecisiveEliminationReplay : IReplayPolicy
{
    public FinalKillEvent? Select(GameModeContext context, ModeResolution resolution, ScoredElimination cause) =>
        resolution.Reason == EndReason.SCORE && context.Rules.Score == ScoreMetric.KILLS
            ? FinalKillEvent.Capture(context.Tick, cause.Kill, cause.Death, context.World.Explosions)
            : null;
}
