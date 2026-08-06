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

    [UiProperty("Win Condition")]
    [MatchRule]
    public WinCondition WinCondition { get; set; } = WinCondition.KILLS;

    [UiProperty("Kill Target", min: 1, max: 999)]
    [UiVisibleWhen(nameof(KillTargetIsRelevant))]
    [MatchRule(min: 1, max: 999)]
    public int KillTarget { get; set; } = SimConfig.KILL_TARGET;

    [UiProperty("Kill Lead Target", min: 1, max: 999)]
    [UiVisibleWhen(nameof(KillLeadTargetIsRelevant))]
    [MatchRule(min: 1, max: 999)]
    public int KillLeadTarget { get; set; } = SimConfig.KILL_LEAD_TARGET;

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

    public bool KillTargetIsRelevant => WinCondition == WinCondition.KILLS;

    public bool KillLeadTargetIsRelevant => WinCondition == WinCondition.KILL_LEAD;

    public bool FriendlyFireIsRelevant => Teams;

    public byte[] ToBytes() => Serialize(this);

    public static ModeRules FromBytes(byte[] data) => Deserialize(data);
}
