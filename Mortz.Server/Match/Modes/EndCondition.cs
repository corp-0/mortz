using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match.Modes;

public enum EndReason { SCORE, TIME, OBJECTIVE }

public readonly record struct EndDecision(EndReason Reason, Victor? Qualifier = null);

public abstract partial class EndCondition
{
    /// <summary>May be evaluated repeatedly within a tick; evaluation must not consume events.</summary>
    public abstract EndDecision? Evaluate(GameModeContext context, IReadOnlyList<ContenderScore> scores);

    public virtual MatchStanding Standing(GameModeContext context, IReadOnlyList<ContenderScore> scores) =>
        new(null, -1);
}

[EndConditionFor(typeof(ScoreTargetRules))]
public class ScoreTargetCondition(ScoreTargetRules rules) : EndCondition
{
    private readonly int _target = rules.Target;

    public override EndDecision? Evaluate(GameModeContext context, IReadOnlyList<ContenderScore> scores) =>
        scores.Any(score => score.Value >= _target) ? new EndDecision(EndReason.SCORE) : null;

    public override MatchStanding Standing(GameModeContext context, IReadOnlyList<ContenderScore> scores)
    {
        int best = 0;
        Victor? leader = null;
        foreach (ContenderScore score in scores)
        {
            if (score.Value <= best)
                continue;
            best = score.Value;
            leader = score.Contender;
        }
        return new MatchStanding(leader, Math.Max(0, _target - best));
    }
}

[EndConditionFor(typeof(ScoreLeadRules))]
public class ScoreLeadCondition(ScoreLeadRules rules) : EndCondition
{
    private readonly int _target = rules.Target;

    public override EndDecision? Evaluate(GameModeContext context, IReadOnlyList<ContenderScore> scores) =>
        Standing(context, scores).Remaining == 0 ? new EndDecision(EndReason.SCORE) : null;

    public override MatchStanding Standing(GameModeContext context, IReadOnlyList<ContenderScore> scores)
    {
        LeadRace race = new(_target);
        foreach (ContenderScore score in scores)
        {
            race.Offer(score.Contender, score.Value);
        }
        return race.Standing();
    }
}

[EndConditionFor(typeof(TimeLimitRules))]
public class TimeLimitCondition(TimeLimitRules rules) : EndCondition
{
    private readonly int _ticks = Math.Max(1, (int)Math.Ceiling(rules.Seconds * SimConfig.TICK_RATE));

    public override EndDecision? Evaluate(GameModeContext context, IReadOnlyList<ContenderScore> scores) =>
        context.Tick >= _ticks ? new EndDecision(EndReason.TIME) : null;
}
