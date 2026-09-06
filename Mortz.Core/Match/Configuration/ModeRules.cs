using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed partial class ModeRules
{
    [UiCategory("Mode")]
    [UiProperty("Teams")]
    [MatchRule]
    public bool Teams { get; set; }

    [ConfigValue(typeof(EndConditionRulesSnapshot), typeof(EndConditionRulesProjection))]
    public EndConditionRules EndCondition { get; set; } = new ScoreTargetRules();

    [UiProperty("Score")]
    [MatchRule]
    public ScoreMetric Score { get; set; } = ScoreMetric.KILLS;

    [UiProperty("Winner")]
    [MatchRule]
    public WinnerRule Winner { get; set; } = WinnerRule.HIGHEST_SCORE;

    [UiProperty("Evaluate Result")]
    [MatchRule]
    public EvaluationTiming Evaluation { get; set; } = EvaluationTiming.ELIMINATION;

    [UiProperty("Final Replay")]
    [MatchRule]
    public ReplayRule Replay { get; set; } = ReplayRule.DECISIVE_ELIMINATION;

    // Self-damage always applies.
    [UiProperty("Friendly Fire")]
    [UiVisibleWhen(nameof(FriendlyFireIsRelevant))]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public SuicidePenalty SuicidePenalty { get; set; } = SuicidePenalty.NONE;

    [UiProperty("Spectate During Respawn")]
    [MatchRule]
    public bool SpectateDuringRespawn { get; set; } = true;

    [UiCategory("Respawn")]
    [UiProperty("Respawn Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float RespawnDelay { get; set; } = SimConfig.RESPAWN_DELAY;

    [UiProperty("Spawn Immunity", min: 0, max: 4, step: 0.05f)]
    [MatchRule(min: 0, max: 4)]
    public float SpawnImmunity { get; set; } = SimConfig.SPAWN_IMMUNITY;

    public int RespawnDelayTicks => (int)(RespawnDelay * SimConfig.TICK_RATE);

    public int SpawnImmunityTicks => (int)(SpawnImmunity * SimConfig.TICK_RATE);

    public bool FriendlyFireIsRelevant => Teams;

}
