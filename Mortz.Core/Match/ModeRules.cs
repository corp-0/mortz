using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match;

public sealed partial class ModeRules
{
    // Keep the win condition when teams are disabled.
    [UiCategory("Mode")]
    [UiProperty("Teams")]
    [MatchRule]
    public bool Teams { get; set; }

    [UiProperty("Win Condition")]
    [MatchRule]
    public WinCondition WinCondition { get; set; } = WinCondition.PLAYER_KILLS;

    [UiProperty("Kill Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int KillTarget { get; set; } = SimConfig.KILL_TARGET;

    // Self-damage always applies.
    [UiProperty("Friendly Fire")]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public SuicidePenalty SuicidePenalty { get; set; } = SuicidePenalty.NONE;

    public byte[] ToBytes() => Serialize(this);

    public static ModeRules FromBytes(byte[] data) => Deserialize(data);
}
