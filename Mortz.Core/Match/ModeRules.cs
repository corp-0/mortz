using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match;

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

    // Self-damage always applies.
    [UiProperty("Friendly Fire")]
    [UiVisibleWhen(nameof(FriendlyFireIsRelevant))]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public SuicidePenalty SuicidePenalty { get; set; } = SuicidePenalty.NONE;

    internal bool KillTargetIsRelevant => WinCondition == WinCondition.KILLS;

    internal bool FriendlyFireIsRelevant => Teams;

    public byte[] ToBytes() => Serialize(this);

    public static ModeRules FromBytes(byte[] data) => Deserialize(data);
}
