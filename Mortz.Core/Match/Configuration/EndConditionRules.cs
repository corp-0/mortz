using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[EndConditionCase("score_target", "Score Target", typeof(ScoreTargetRules))]
[EndConditionCase("score_lead", "Score Lead", typeof(ScoreLeadRules))]
[EndConditionCase("time_limit", "Time Limit", typeof(TimeLimitRules))]
public abstract class EndConditionRules
{
    public abstract EndConditionRulesSnapshot ToSnapshot();
}

public sealed partial class ScoreTargetRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = SimConfig.KILL_TARGET;
}

public sealed partial class ScoreLeadRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Lead Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = SimConfig.KILL_LEAD_TARGET;
}

public partial class TimeLimitRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Time Limit (seconds)", min: 1, max: 86400)]
    [MatchRule(min: 1, max: 86400)]
    public float Seconds { get; set; } = 180;
}
