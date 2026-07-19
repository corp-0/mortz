using System.Text.Json;
using System.Text.Json.Serialization;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match;

/// <summary>
/// The host-tweakable half of the ruleset. Every value here is match data:
/// fixed once the match starts, replicated to clients in the Welcome so
/// prediction runs the exact numbers the server runs. Defaults are the
/// SimConfig constants; load-bearing infrastructure (tick rate, hitbox,
/// net rates) stays const there and is deliberately not in here.
///
/// Each property is the single declaration of its field: [PlayerStat] for
/// per-player, modifier-targetable stats, [MatchRule] for match-level
/// rules. ConfigGenerator expands them into the Stat enum, Clamp(), the
/// wire, PlayerStats and the pipeline switches. Clamp()
/// runs wherever a config enters the process (preset load, wire receipt),
/// so a broken or hostile config can't produce a degenerate sim; ranges
/// also keep tick-count values inside the byte fields PlayerState stores
/// them in.
///
/// The wire blob is field-by-field, hidden inside WelcomeMsg's byte[]
/// where NetRegistry.SCHEMA_HASH can't see it: adding, removing or
/// reordering fields needs a PROTOCOL_VERSION bump.
/// </summary>
public sealed partial class MatchConfig
{
    // Teams and WinCondition are stored independently so toggling teams in the
    // lobby never destroys the admin's win condition choice; TEAM_KILLS with
    // teams off plays as PLAYER_KILLS (resolved in Scoreboard, not here).
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

    /// <summary>Off spares teammates from blast damage; self-damage always applies.</summary>
    [UiProperty("Friendly Fire")]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    /// <summary>On, a suicide costs a kill (scores can go negative).</summary>
    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public bool SuicidePenalty { get; set; }

    [UiCategory("Running / Falling")]
    [UiProperty("Max Run Speed", min: 40, max: 2000, step: 10)]
    [PlayerStat(min: 40, max: 2000)]
    public float MaxRunSpeed { get; set; } = SimConfig.MAX_RUN_SPEED;

    [UiProperty("Ground Acceleration", min: 200, max: 50000, step: 100)]
    [PlayerStat(min: 200, max: 50000)]
    public float GroundAccel { get; set; } = SimConfig.GROUND_ACCEL;

    // min 0 = ice match
    [UiProperty("Ground Friction", min: 0, max: 50000, step: 100)]
    [PlayerStat(min: 0, max: 50000)]
    public float GroundFriction { get; set; } = SimConfig.GROUND_FRICTION;

    [UiProperty("Air Acceleration", min: 0, max: 50000, step: 100)]
    [PlayerStat(min: 0, max: 50000)]
    public float AirAccel { get; set; } = SimConfig.AIR_ACCEL;

    [UiProperty("Gravity", min: 100, max: 8000, step: 50)]
    [PlayerStat(min: 100, max: 8000)]
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


    [UiCategory("Mortar")]
    [UiProperty("Mortar Speed", min: 100, max: 4000, step: 50)]
    [MatchRule(min: 100, max: 4000)]
    public float MortarSpeed { get; set; } = SimConfig.MORTAR_SPEED;

    [UiProperty("Inherited Velocity", min: 0, max: 2, step: 0.05f)]
    [MatchRule(min: 0, max: 2)]
    public float MortarInherit { get; set; } = SimConfig.MORTAR_INHERIT;

    // min 0 = laser shells
    [UiProperty("Mortar Gravity", min: 0, max: 8000, step: 50)]
    [MatchRule(min: 0, max: 8000)]
    public float MortarGravity { get; set; } = SimConfig.MORTAR_GRAVITY;

    [UiProperty("Mortar Max Fall Speed", min: 100, max: 4000, step: 50)]
    [MatchRule(min: 100, max: 4000)]
    public float MortarMaxFall { get; set; } = SimConfig.MORTAR_MAX_FALL;

    // max caps carve cost, which is O(r^2)
    [UiProperty("Carve Radius", min: 8, max: 128)]
    [MatchRule(min: 8, max: 128)]
    public int MortarCarveRadius { get; set; } = SimConfig.MORTAR_CARVE_RADIUS;

    [UiProperty("Max Ammo", min: 1, max: 30)]
    [PlayerStat(min: 1, max: 30,
        convert: StatConvert.COUNT_BYTE, statsName: "MaxAmmo")]
    public int MortarMaxAmmo { get; set; } = SimConfig.MORTAR_MAX_AMMO;

    [UiProperty("Reload Per Shell", min: 0.1f, max: 4, step: 0.05f)]
    [PlayerStat(min: 0.1f, max: 4,
        convert: StatConvert.TICKS_BYTE, statsName: "ReloadPerShell")]
    public float MortarReloadPerShell { get; set; } = SimConfig.MORTAR_RELOAD_PER_SHELL;

    [UiCategory("Parry")]
    [UiProperty("Parry Radius", min: 8, max: 200, step: 1)]
    [PlayerStat(min: 8, max: 200)]
    public float ParryRadius { get; set; } = SimConfig.PARRY_RADIUS;

    [UiProperty("Parry Window", min: 0, max: 4, step: 0.05f)]
    [PlayerStat(min: 0, max: 4,
        convert: StatConvert.TICKS_BYTE)]
    public float ParryWindow { get; set; } = SimConfig.PARRY_WINDOW;

    [UiProperty("Parry Cooldown", min: 0, max: 120, step: 0.5f)]
    [PlayerStat(min: 0, max: 120,
        convert: StatConvert.TICKS_USHORT)]
    public float ParryCooldown { get; set; } = SimConfig.PARRY_COOLDOWN;

    [UiCategory("Health / Blast")]
    [UiProperty("Max Health", min: 1, max: 250)]
    [PlayerStat(min: 1, max: 250,
        convert: StatConvert.COUNT_BYTE)]
    public int MaxHealth { get; set; } = SimConfig.MAX_HEALTH;

    [UiProperty("Mortar Damage", min: 0, max: 250)]
    [MatchRule(min: 0, max: 250)]
    public int MortarDamage { get; set; } = SimConfig.MORTAR_DAMAGE;

    [UiProperty("Blast Core Fraction", min: 0, max: 1, step: 0.05f)]
    [MatchRule(min: 0, max: 1)]
    public float BlastCoreFraction { get; set; } = SimConfig.BLAST_CORE_FRACTION;

    [UiProperty("Blast Edge Damage", min: 0, max: 250)]
    [MatchRule(min: 0, max: 250)]
    public int BlastEdgeDamage { get; set; } = SimConfig.BLAST_EDGE_DAMAGE;

    [UiProperty("Respawn Delay", min: 0, max: 4, step: 0.05f)]
    [MatchRule(min: 0, max: 4)]
    public float RespawnDelay { get; set; } = SimConfig.RESPAWN_DELAY;

    [UiProperty("Spawn Immunity", min: 0, max: 4, step: 0.05f)]
    [MatchRule(min: 0, max: 4)]
    public float SpawnImmunity { get; set; } = SimConfig.SPAWN_IMMUNITY;

    public int RespawnDelayTicks => (int)(RespawnDelay * SimConfig.TICK_RATE);
    public int SpawnImmunityTicks => (int)(SpawnImmunity * SimConfig.TICK_RATE);

    public byte[] ToBytes() => Serialize(this);

    public static MatchConfig FromBytes(byte[] data) => Deserialize(data);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Ruleset preset: JSON with any subset of the properties;
    /// everything omitted keeps its default. Throws JsonException on garbage.</summary>
    public static MatchConfig FromJson(string json)
    {
        MatchConfig cfg = JsonSerializer.Deserialize<MatchConfig>(json, _jsonOptions)
                          ?? throw new JsonException("ruleset is empty");
        cfg.Clamp();
        return cfg;
    }
}
