using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mortz.Core;

/// <summary>What the score predicate reads: individual rows or team totals.
/// In ruleset JSON: "PLAYER_KILLS" / "TEAM_KILLS" (case-insensitive).</summary>
public enum WinCondition : byte
{
    PLAYER_KILLS = 0,
    TEAM_KILLS = 1,
}

/// <summary>
/// The host-tweakable half of the ruleset. Every value here is match data:
/// fixed once the match starts, replicated to clients in the Welcome so
/// prediction runs the exact numbers the server runs. Defaults are the
/// SimConfig constants; load-bearing infrastructure (tick rate, hitbox,
/// net rates) stays const there and is deliberately not in here.
///
/// Clamp() runs wherever a config enters the process (preset load, wire
/// receipt), so a broken or hostile config can't produce a degenerate sim.
/// Ranges also keep the tick-count values inside the byte fields PlayerState
/// stores them in.
///
/// The wire blob is field-by-field in declaration order, hidden inside
/// WelcomeMsg's byte[] where NetRegistry.SCHEMA_HASH can't see it: adding,
/// removing or reordering fields needs a PROTOCOL_VERSION bump.
/// </summary>
public sealed class MatchConfig
{
    // ---- running / falling ----
    public float MaxRunSpeed { get; set; } = SimConfig.MAX_RUN_SPEED;
    public float GroundAccel { get; set; } = SimConfig.GROUND_ACCEL;
    public float GroundFriction { get; set; } = SimConfig.GROUND_FRICTION;
    public float AirAccel { get; set; } = SimConfig.AIR_ACCEL;
    public float Gravity { get; set; } = SimConfig.GRAVITY;
    public float MaxFallSpeed { get; set; } = SimConfig.MAX_FALL_SPEED;

    // ---- jumps ----
    public int TotalJumps { get; set; } = SimConfig.TOTAL_JUMPS;
    public float JumpSpeed { get; set; } = SimConfig.JUMP_SPEED;
    public float AirJumpSpeed { get; set; } = SimConfig.AIR_JUMP_SPEED;
    public float WallSlideMaxFall { get; set; } = SimConfig.WALL_SLIDE_MAX_FALL;
    public float WallJumpSpeedY { get; set; } = SimConfig.WALL_JUMP_SPEED_Y;
    public float WallJumpKickX { get; set; } = SimConfig.WALL_JUMP_KICK_X;
    public float CoyoteBase { get; set; } = SimConfig.COYOTE_BASE;
    public float CoyoteBonusPer100Speed { get; set; } = SimConfig.COYOTE_BONUS_PER_100_SPEED;
    public float CoyoteMax { get; set; } = SimConfig.COYOTE_MAX;

    // ---- dash ----
    public float DashSpeed { get; set; } = SimConfig.DASH_SPEED;
    public float DashCooldown { get; set; } = SimConfig.DASH_COOLDOWN;

    // ---- rope ----
    public float RopeSpeed { get; set; } = SimConfig.ROPE_SPEED;
    public float RopeMaxRange { get; set; } = SimConfig.ROPE_MAX_RANGE;
    public float RopePullAccel { get; set; } = SimConfig.ROPE_PULL_ACCEL;
    public float RopeShortenSpeed { get; set; } = SimConfig.ROPE_SHORTEN_SPEED;
    public float RopeReleaseCooldown { get; set; } = SimConfig.ROPE_RELEASE_COOLDOWN;
    public float RopeMissCooldown { get; set; } = SimConfig.ROPE_MISS_COOLDOWN;

    // ---- mortar ----
    public float MortarSpeed { get; set; } = SimConfig.MORTAR_SPEED;
    public float MortarInherit { get; set; } = SimConfig.MORTAR_INHERIT;
    public float MortarGravity { get; set; } = SimConfig.MORTAR_GRAVITY;
    public float MortarMaxFall { get; set; } = SimConfig.MORTAR_MAX_FALL;
    public int MortarCarveRadius { get; set; } = SimConfig.MORTAR_CARVE_RADIUS;
    public int MortarMaxAmmo { get; set; } = SimConfig.MORTAR_MAX_AMMO;
    public float MortarReloadPerShell { get; set; } = SimConfig.MORTAR_RELOAD_PER_SHELL;

    // ---- parry ----
    public float ParryRadius { get; set; } = SimConfig.PARRY_RADIUS;
    public float ParryWindow { get; set; } = SimConfig.PARRY_WINDOW;
    public float ParryCooldown { get; set; } = SimConfig.PARRY_COOLDOWN;

    // ---- health / blast ----
    public int MaxHealth { get; set; } = SimConfig.MAX_HEALTH;
    public int MortarDamage { get; set; } = SimConfig.MORTAR_DAMAGE;
    public float BlastCoreFraction { get; set; } = SimConfig.BLAST_CORE_FRACTION;
    public int BlastEdgeDamage { get; set; } = SimConfig.BLAST_EDGE_DAMAGE;
    public float RespawnDelay { get; set; } = SimConfig.RESPAWN_DELAY;
    public float SpawnImmunity { get; set; } = SimConfig.SPAWN_IMMUNITY;

    // ---- mode ----
    // Teams and WinCondition are stored independently so toggling teams in the
    // lobby never destroys the admin's win condition choice; TEAM_KILLS with
    // teams off plays as PLAYER_KILLS (resolved in Scoreboard, not here).
    public bool Teams { get; set; }
    public WinCondition WinCondition { get; set; } = WinCondition.PLAYER_KILLS;
    public int KillTarget { get; set; } = SimConfig.KILL_TARGET;
    /// <summary>Off spares teammates from blast damage; self-damage always applies.</summary>
    public bool FriendlyFire { get; set; } = true;
    /// <summary>On, a suicide costs a kill (scores can go negative).</summary>
    public bool SuicidePenalty { get; set; }

    public int RespawnDelayTicks => (int)(RespawnDelay * SimConfig.TICK_RATE);
    public int SpawnImmunityTicks => (int)(SpawnImmunity * SimConfig.TICK_RATE);

    /// <summary>Force every field into its sane range; NaN lands on the minimum.</summary>
    public void Clamp()
    {
        MaxRunSpeed = C(MaxRunSpeed, 40, 2000);
        GroundAccel = C(GroundAccel, 200, 50000);
        GroundFriction = C(GroundFriction, 0, 50000); // 0 = ice match
        AirAccel = C(AirAccel, 0, 50000);
        Gravity = C(Gravity, 100, 8000);
        MaxFallSpeed = C(MaxFallSpeed, 100, 4000);

        TotalJumps = Math.Clamp(TotalJumps, 1, 10);
        JumpSpeed = C(JumpSpeed, 0, 3000);
        AirJumpSpeed = C(AirJumpSpeed, 0, 3000);
        WallSlideMaxFall = C(WallSlideMaxFall, 20, 4000);
        WallJumpSpeedY = C(WallJumpSpeedY, 0, 3000);
        WallJumpKickX = C(WallJumpKickX, 0, 3000);
        CoyoteBase = C(CoyoteBase, 0, 0.5f);
        CoyoteBonusPer100Speed = C(CoyoteBonusPer100Speed, 0, 0.2f);
        CoyoteMax = C(CoyoteMax, 0, 1);

        DashSpeed = C(DashSpeed, 0, 3000);
        DashCooldown = C(DashCooldown, 0, 4);

        RopeSpeed = C(RopeSpeed, 200, 5000);
        RopeMaxRange = C(RopeMaxRange, 50, 2000);
        RopePullAccel = C(RopePullAccel, 200, 20000);
        RopeShortenSpeed = C(RopeShortenSpeed, 0, 1000);
        RopeReleaseCooldown = C(RopeReleaseCooldown, 0, 4);
        RopeMissCooldown = C(RopeMissCooldown, 0, 4);

        MortarSpeed = C(MortarSpeed, 100, 4000);
        MortarInherit = C(MortarInherit, 0, 2);
        MortarGravity = C(MortarGravity, 0, 8000); // 0 = laser shells
        MortarMaxFall = C(MortarMaxFall, 100, 4000);
        MortarCarveRadius = Math.Clamp(MortarCarveRadius, 8, 128); // carve cost is O(r^2)
        MortarMaxAmmo = Math.Clamp(MortarMaxAmmo, 1, 30);
        MortarReloadPerShell = C(MortarReloadPerShell, 0.1f, 4);

        ParryRadius = C(ParryRadius, 8, 200);
        ParryWindow = C(ParryWindow, 0, 4);       // byte ticks in PlayerState
        ParryCooldown = C(ParryCooldown, 0, 120); // ushort ticks in PlayerState

        MaxHealth = Math.Clamp(MaxHealth, 1, 250);
        MortarDamage = Math.Clamp(MortarDamage, 0, 250);
        BlastCoreFraction = C(BlastCoreFraction, 0, 1);
        BlastEdgeDamage = Math.Clamp(BlastEdgeDamage, 0, 250);
        RespawnDelay = C(RespawnDelay, 0, 4);
        SpawnImmunity = C(SpawnImmunity, 0, 4);

        if (!Enum.IsDefined(WinCondition))
            WinCondition = WinCondition.PLAYER_KILLS;
        KillTarget = Math.Clamp(KillTarget, 1, 999);
    }

    private static float C(float v, float min, float max) =>
        float.IsNaN(v) ? min : Math.Clamp(v, min, max);

    public byte[] ToBytes() => MatchConfigWire.Serialize(this);

    public static MatchConfig FromBytes(byte[] data) => MatchConfigWire.Deserialize(data);

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
