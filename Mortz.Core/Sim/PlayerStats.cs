using Mortz.Core.Match;

namespace Mortz.Core.Sim;

/// <summary>
/// One player's resolved sim numbers: the match config's per-player bases
/// with that player's perk multipliers applied. No perks exist yet, so today
/// this is a straight copy. Resolved once at spawn and cached; the sim reads
/// these and never MatchConfig directly, which is what makes perks a change
/// to this file only. Seconds become ticks here, once, sized for the byte
/// counters in PlayerState (the config clamps guarantee they fit).
/// </summary>
public sealed class PlayerStats
{
    public readonly float MaxRunSpeed;
    public readonly float GroundAccel;
    public readonly float GroundFriction;
    public readonly float AirAccel;
    public readonly float Gravity;
    public readonly float MaxFallSpeed;

    public readonly byte TotalJumps;
    public readonly float JumpSpeed;
    public readonly float AirJumpSpeed;
    public readonly float WallSlideMaxFall;
    public readonly float WallJumpSpeedY;
    public readonly float WallJumpKickX;
    public readonly int CoyoteBaseTicks;
    public readonly float CoyoteBonusPer100Speed;
    public readonly int CoyoteMaxTicks;

    public readonly float DashSpeed;
    public readonly byte DashCooldownTicks;

    public readonly float RopeSpeed;
    public readonly float RopeMaxRange;
    public readonly float RopePullAccel;
    public readonly float RopeShortenSpeed;
    public readonly byte RopeReleaseCooldownTicks;
    public readonly byte RopeMissCooldownTicks;

    public readonly byte MaxAmmo;
    public readonly byte ReloadTicks;
    public readonly byte MaxHealth;

    public readonly float ParryRadius;
    public readonly byte ParryWindowTicks;
    public readonly ushort ParryCooldownTicks;

    /// <summary>Perk multipliers slot in here once perks exist: base times
    /// each perk's factor, re-clamped, then the tick/byte casts below.</summary>
    public static PlayerStats Resolve(MatchConfig cfg) => new(cfg);

    private PlayerStats(MatchConfig cfg)
    {
        MaxRunSpeed = cfg.MaxRunSpeed;
        GroundAccel = cfg.GroundAccel;
        GroundFriction = cfg.GroundFriction;
        AirAccel = cfg.AirAccel;
        Gravity = cfg.Gravity;
        MaxFallSpeed = cfg.MaxFallSpeed;

        TotalJumps = (byte)cfg.TotalJumps;
        JumpSpeed = cfg.JumpSpeed;
        AirJumpSpeed = cfg.AirJumpSpeed;
        WallSlideMaxFall = cfg.WallSlideMaxFall;
        WallJumpSpeedY = cfg.WallJumpSpeedY;
        WallJumpKickX = cfg.WallJumpKickX;
        CoyoteBaseTicks = Ticks(cfg.CoyoteBase);
        CoyoteBonusPer100Speed = cfg.CoyoteBonusPer100Speed;
        CoyoteMaxTicks = Ticks(cfg.CoyoteMax);

        DashSpeed = cfg.DashSpeed;
        DashCooldownTicks = (byte)Ticks(cfg.DashCooldown);

        RopeSpeed = cfg.RopeSpeed;
        RopeMaxRange = cfg.RopeMaxRange;
        RopePullAccel = cfg.RopePullAccel;
        RopeShortenSpeed = cfg.RopeShortenSpeed;
        RopeReleaseCooldownTicks = (byte)Ticks(cfg.RopeReleaseCooldown);
        RopeMissCooldownTicks = (byte)Ticks(cfg.RopeMissCooldown);

        MaxAmmo = (byte)cfg.MortarMaxAmmo;
        ReloadTicks = (byte)Ticks(cfg.MortarReloadPerShell);
        MaxHealth = (byte)cfg.MaxHealth;

        ParryRadius = cfg.ParryRadius;
        ParryWindowTicks = (byte)Ticks(cfg.ParryWindow);
        ParryCooldownTicks = (ushort)Ticks(cfg.ParryCooldown);
    }

    private static int Ticks(float seconds) => (int)(seconds * SimConfig.TICK_RATE);
}
