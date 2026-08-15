using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed partial class Physics
{
    [UiCategory("Running / Falling")]
    [UiProperty("Max Run Speed", min: 40, max: 2000, step: 10)]
    [PlayerStat(min: 40, max: 2000)]
    public float MaxRunSpeed { get; set; } = SimConfig.MAX_RUN_SPEED;

    [UiProperty("Ground Acceleration", min: 200, max: 50000, step: 100)]
    [PlayerStat(min: 200, max: 50000)]
    public float GroundAccel { get; set; } = SimConfig.GROUND_ACCEL;

    // Zero friction makes ice.
    [UiProperty("Ground Friction", min: 0, max: 50000, step: 100)]
    [PlayerStat(min: 0, max: 50000)]
    public float GroundFriction { get; set; } = SimConfig.GROUND_FRICTION;

    [UiProperty("Air Acceleration", min: 0, max: 50000, step: 100)]
    [PlayerStat(min: 0, max: 50000)]
    public float AirAccel { get; set; } = SimConfig.AIR_ACCEL;

    [UiProperty("Gravity", min: -8000, max: 8000, step: 50)]
    [PlayerStat(min: -8000, max: 8000)]
    public float Gravity { get; set; } = SimConfig.GRAVITY;

    [UiProperty("Max Fall Speed", min: 100, max: 4000, step: 50)]
    [PlayerStat(min: 100, max: 4000)]
    public float MaxFallSpeed { get; set; } = SimConfig.MAX_FALL_SPEED;

    [UiCategory("Jumps")]
    [UiProperty("Total Jumps", min: 1, max: 10)]
    [PlayerStat(min: 1, max: 10,
        convert: StatConvert.COUNT_BYTE)]
    public int TotalJumps { get; set; } = SimConfig.TOTAL_JUMPS;

    [UiProperty("Jump Speed", min: 0, max: 3000, step: 25)]
    [PlayerStat(min: 0, max: 3000)]
    public float JumpSpeed { get; set; } = SimConfig.JUMP_SPEED;

    [UiProperty("Air Jump Speed", min: 0, max: 3000, step: 25)]
    [PlayerStat(min: 0, max: 3000)]
    public float AirJumpSpeed { get; set; } = SimConfig.AIR_JUMP_SPEED;

    [UiProperty("Wall Slide Max Fall", min: 20, max: 4000, step: 50)]
    [PlayerStat(min: 20, max: 4000)]
    public float WallSlideMaxFall { get; set; } = SimConfig.WALL_SLIDE_MAX_FALL;

    [UiProperty("Wall Jump Speed", min: 0, max: 3000, step: 25)]
    [PlayerStat(min: 0, max: 3000)]
    public float WallJumpSpeedY { get; set; } = SimConfig.WALL_JUMP_SPEED_Y;

    [UiProperty("Wall Jump Kick", min: 0, max: 3000, step: 25)]
    [PlayerStat(min: 0, max: 3000)]
    public float WallJumpKickX { get; set; } = SimConfig.WALL_JUMP_KICK_X;

    [UiProperty("Coyote Time", min: 0, max: 0.5f)]
    [PlayerStat(min: 0, max: 0.5f,
        convert: StatConvert.TICKS_INT)]
    public float CoyoteBase { get; set; } = SimConfig.COYOTE_BASE;

    [UiProperty("Coyote Bonus Per 100 Speed", min: 0, max: 0.2f, step: 0.005f)]
    [PlayerStat(min: 0, max: 0.2f)]
    public float CoyoteBonusPer100Speed { get; set; } = SimConfig.COYOTE_BONUS_PER_100_SPEED;

    [UiProperty("Max Coyote Time", min: 0, max: 1)]
    [PlayerStat(min: 0, max: 1,
        convert: StatConvert.TICKS_INT)]
    public float CoyoteMax { get; set; } = SimConfig.COYOTE_MAX;

    [UiCategory("Dash")]
    [UiProperty("Dash Speed", min: 0, max: 3000, step: 25)]
    [PlayerStat(min: 0, max: 3000)]
    public float DashSpeed { get; set; } = SimConfig.DASH_SPEED;

    [UiProperty("Dash Cooldown", min: 0, max: 4, step: 0.05f)]
    [PlayerStat(min: 0, max: 4,
        convert: StatConvert.TICKS_BYTE)]
    public float DashCooldown { get; set; } = SimConfig.DASH_COOLDOWN;
    [UiCategory("Rope")]
    [UiProperty("Rope Speed", min: 200, max: 5000, step: 50)]
    [PlayerStat(min: 200, max: 5000)]
    public float RopeSpeed { get; set; } = SimConfig.ROPE_SPEED;

    [UiProperty("Rope Max Range", min: 50, max: 2000, step: 25)]
    [PlayerStat(min: 50, max: 2000)]
    public float RopeMaxRange { get; set; } = SimConfig.ROPE_MAX_RANGE;

    [UiProperty("Rope Pull Acceleration", min: 200, max: 20000, step: 100)]
    [PlayerStat(min: 200, max: 20000)]
    public float RopePullAccel { get; set; } = SimConfig.ROPE_PULL_ACCEL;

    [UiProperty("Rope Shorten Speed", min: 0, max: 1000, step: 10)]
    [PlayerStat(min: 0, max: 1000)]
    public float RopeShortenSpeed { get; set; } = SimConfig.ROPE_SHORTEN_SPEED;

    [UiProperty("Rope Release Cooldown", min: 0, max: 4, step: 0.05f)]
    [PlayerStat(min: 0, max: 4,
        convert: StatConvert.TICKS_BYTE)]
    public float RopeReleaseCooldown { get; set; } = SimConfig.ROPE_RELEASE_COOLDOWN;

    [UiProperty("Rope Miss Cooldown", min: 0, max: 4, step: 0.05f)]
    [PlayerStat(min: 0, max: 4,
        convert: StatConvert.TICKS_BYTE)]
    public float RopeMissCooldown { get; set; } = SimConfig.ROPE_MISS_COOLDOWN;

}
