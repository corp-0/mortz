using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match.Modes;

/// <summary>Composes scoring, end conditions, winner selection, and replay into a match outcome.</summary>
public class GameMode(
    IScoreSource scores,
    EndCondition endCondition,
    IWinnerSelection winner,
    EvaluationTiming timing,
    IReplayPolicy replay)
{
    public static GameMode Create(ModeRulesSnapshot rules) => new(
        ScoreSources.Create(rules.Score), EndCondition.Create(rules.EndCondition.ToMutable()),
        WinnerSelections.Create(rules.Winner), rules.Evaluation, ReplayPolicies.Create(rules.Replay));

    public GameModeUpdate Advance(MatchContext match, ScoringStep scoring, IReadOnlyList<Death> deaths)
    {
        List<ScoredKill> eliminations = [];
        GameModeContext Context() => new(match.World, scoring.Rows(), scoring.TeamKills, scoring.TeamDeaths);
        GameModeUpdate Complete(MatchOutcome? outcome) => new(eliminations, Standing(Context()), outcome);

        if (timing == EvaluationTiming.TICK)
        {
            eliminations.AddRange(scoring.Apply(match, deaths));
            return Complete(Evaluate(Context()));
        }

        // A timer or roster change can finish the match before any death is scored.
        if (Evaluate(Context()) is MatchOutcome existing)
            return Complete(existing);

        foreach (Death death in deaths)
        {
            if (scoring.ScoreDeath(match, death) is not ScoredKill elimination)
                continue;
            eliminations.Add(elimination);
            GameModeContext context = Context();
            if (Resolve(context) is not ModeResolution resolution)
                continue;
            FinalKillEvent? finalKill = replay.Select(context, resolution,
                new ScoredElimination(elimination, death));
            return Complete(new MatchOutcome(resolution.Winner, finalKill));
        }

        return Complete(null);
    }

    /// <summary>Null keeps the match running, including when winner selection cannot break a tie.</summary>
    public MatchOutcome? Evaluate(GameModeContext context) =>
        Resolve(context) is ModeResolution resolution ? new MatchOutcome(resolution.Winner) : null;

    public MatchStanding Standing(GameModeContext context)
    {
        IReadOnlyList<ContenderScore> contenders = scores.Read(context);
        MatchStanding standing = endCondition.Standing(context, contenders);
        // Match-point announcements currently describe a winning kill.
        return standing.Remaining >= 0 && context.Rules.Score == ScoreMetric.KILLS &&
               context.Rules.Winner == WinnerRule.HIGHEST_SCORE
            ? standing
            : new MatchStanding(winner.Select(contenders, default), -1);
    }

    private ModeResolution? Resolve(GameModeContext context)
    {
        IReadOnlyList<ContenderScore> contenders = scores.Read(context);
        if (endCondition.Evaluate(context, contenders) is not EndDecision decision ||
            winner.Select(contenders, decision) is not Victor victor)
            return null;
        return new ModeResolution(victor, decision.Reason);
    }
}

/// <summary>No final-kill replay is implied by a winner or a death on the ending tick.</summary>
public record MatchOutcome(Victor Winner, FinalKillEvent? FinalKill = null);

public readonly record struct GameModeUpdate(
    IReadOnlyList<ScoredKill> Eliminations, MatchStanding Standing, MatchOutcome? Outcome);

public readonly record struct ModeResolution(Victor Winner, EndReason Reason);

public readonly record struct ScoredElimination(ScoredKill Kill, Death Death);
