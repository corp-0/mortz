using System.Text.Json;

namespace Mortz.Core;

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

    public int RespawnDelayTicks => (int)(RespawnDelay * SimConfig.TICK_RATE);

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
    }

    private static float C(float v, float min, float max) =>
        float.IsNaN(v) ? min : Math.Clamp(v, min, max);

    public byte[] ToBytes()
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write(MaxRunSpeed);
        w.Write(GroundAccel);
        w.Write(GroundFriction);
        w.Write(AirAccel);
        w.Write(Gravity);
        w.Write(MaxFallSpeed);
        w.Write(TotalJumps);
        w.Write(JumpSpeed);
        w.Write(AirJumpSpeed);
        w.Write(WallSlideMaxFall);
        w.Write(WallJumpSpeedY);
        w.Write(WallJumpKickX);
        w.Write(CoyoteBase);
        w.Write(CoyoteBonusPer100Speed);
        w.Write(CoyoteMax);
        w.Write(DashSpeed);
        w.Write(DashCooldown);
        w.Write(RopeSpeed);
        w.Write(RopeMaxRange);
        w.Write(RopePullAccel);
        w.Write(RopeShortenSpeed);
        w.Write(RopeReleaseCooldown);
        w.Write(RopeMissCooldown);
        w.Write(MortarSpeed);
        w.Write(MortarInherit);
        w.Write(MortarGravity);
        w.Write(MortarMaxFall);
        w.Write(MortarCarveRadius);
        w.Write(MortarMaxAmmo);
        w.Write(MortarReloadPerShell);
        w.Write(ParryRadius);
        w.Write(ParryWindow);
        w.Write(ParryCooldown);
        w.Write(MaxHealth);
        w.Write(MortarDamage);
        w.Write(BlastCoreFraction);
        w.Write(BlastEdgeDamage);
        w.Write(RespawnDelay);
        return ms.ToArray();
    }

    public static MatchConfig FromBytes(byte[] data)
    {
        using MemoryStream ms = new(data);
        using BinaryReader r = new(ms);
        MatchConfig cfg = new()
        {
            MaxRunSpeed = r.ReadSingle(),
            GroundAccel = r.ReadSingle(),
            GroundFriction = r.ReadSingle(),
            AirAccel = r.ReadSingle(),
            Gravity = r.ReadSingle(),
            MaxFallSpeed = r.ReadSingle(),
            TotalJumps = r.ReadInt32(),
            JumpSpeed = r.ReadSingle(),
            AirJumpSpeed = r.ReadSingle(),
            WallSlideMaxFall = r.ReadSingle(),
            WallJumpSpeedY = r.ReadSingle(),
            WallJumpKickX = r.ReadSingle(),
            CoyoteBase = r.ReadSingle(),
            CoyoteBonusPer100Speed = r.ReadSingle(),
            CoyoteMax = r.ReadSingle(),
            DashSpeed = r.ReadSingle(),
            DashCooldown = r.ReadSingle(),
            RopeSpeed = r.ReadSingle(),
            RopeMaxRange = r.ReadSingle(),
            RopePullAccel = r.ReadSingle(),
            RopeShortenSpeed = r.ReadSingle(),
            RopeReleaseCooldown = r.ReadSingle(),
            RopeMissCooldown = r.ReadSingle(),
            MortarSpeed = r.ReadSingle(),
            MortarInherit = r.ReadSingle(),
            MortarGravity = r.ReadSingle(),
            MortarMaxFall = r.ReadSingle(),
            MortarCarveRadius = r.ReadInt32(),
            MortarMaxAmmo = r.ReadInt32(),
            MortarReloadPerShell = r.ReadSingle(),
            ParryRadius = r.ReadSingle(),
            ParryWindow = r.ReadSingle(),
            ParryCooldown = r.ReadSingle(),
            MaxHealth = r.ReadInt32(),
            MortarDamage = r.ReadInt32(),
            BlastCoreFraction = r.ReadSingle(),
            BlastEdgeDamage = r.ReadInt32(),
            RespawnDelay = r.ReadSingle(),
        };
        cfg.Clamp();
        return cfg;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
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
