using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class MatchConfigTests
{
    [Fact]
    public void WireBlob_RoundTrips()
    {
        MatchConfig sent = new() { Gravity = 750, MortarMaxAmmo = 8, GroundFriction = 0 };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.Equal(750, got.Gravity);
        Assert.Equal(8, got.MortarMaxAmmo);
        Assert.Equal(0, got.GroundFriction);
        Assert.Equal(SimConfig.MAX_RUN_SPEED, got.MaxRunSpeed); // untouched fields keep defaults
    }

    [Fact]
    public void FromBytes_ClampsHostileValues()
    {
        MatchConfig hostile = new()
        {
            Gravity = float.NaN,
            MortarCarveRadius = 100000,
            MaxHealth = 9999,
            MortarReloadPerShell = -3,
        };
        MatchConfig got = MatchConfig.FromBytes(hostile.ToBytes());

        Assert.Equal(100, got.Gravity); // NaN lands on the minimum
        Assert.Equal(128, got.MortarCarveRadius);
        Assert.Equal(250, got.MaxHealth);
        Assert.Equal(0.1f, got.MortarReloadPerShell);
    }

    [Fact]
    public void ModeFields_RoundTripTheWire_AndClampHostileValues()
    {
        MatchConfig sent = new()
        {
            Teams = true,
            WinCondition = WinCondition.TEAM_KILLS,
            KillTarget = 5,
            FriendlyFire = false,
            SuicidePenalty = true,
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.True(got.Teams);
        Assert.Equal(WinCondition.TEAM_KILLS, got.WinCondition);
        Assert.Equal(5, got.KillTarget);
        Assert.False(got.FriendlyFire);
        Assert.True(got.SuicidePenalty);

        MatchConfig hostile = new() { WinCondition = (WinCondition)99, KillTarget = 0 };
        got = MatchConfig.FromBytes(hostile.ToBytes());
        Assert.Equal(WinCondition.PLAYER_KILLS, got.WinCondition);
        Assert.Equal(1, got.KillTarget);
    }

    [Fact]
    public void FromJson_ParsesWinConditionByName()
    {
        MatchConfig got = MatchConfig.FromJson("""
            {
                "teams": true,
                "winCondition": "team_kills",
                "killTarget": 30,
            }
            """);

        Assert.True(got.Teams);
        Assert.Equal(WinCondition.TEAM_KILLS, got.WinCondition);
        Assert.Equal(30, got.KillTarget);
        Assert.True(got.FriendlyFire); // untouched fields keep defaults
    }

    [Fact]
    public void FromJson_PartialPreset_OverridesOnlyNamedFields()
    {
        MatchConfig got = MatchConfig.FromJson("""
            {
                // low-grav rope match
                "gravity": 600,
                "RopePullAccel": 4000,
            }
            """);

        Assert.Equal(600, got.Gravity);
        Assert.Equal(4000, got.RopePullAccel);
        Assert.Equal(SimConfig.DASH_SPEED, got.DashSpeed);
    }

    [Fact]
    public void DefaultResolvedStats_MatchTheSimConfigConsts()
    {
        PlayerStats stats = PlayerStats.Resolve(new MatchConfig());

        Assert.Equal(SimConfig.MAX_RUN_SPEED, stats.MaxRunSpeed);
        Assert.Equal(SimConfig.TOTAL_JUMPS, stats.TotalJumps);
        Assert.Equal(SimConfig.DASH_COOLDOWN_TICKS, stats.DashCooldownTicks);
        Assert.Equal(SimConfig.MORTAR_RELOAD_TICKS, stats.ReloadTicks);
        Assert.Equal(SimConfig.COYOTE_MAX_TICKS, stats.CoyoteMaxTicks);
        Assert.Equal(SimConfig.MAX_HEALTH, stats.MaxHealth);
        Assert.Equal(SimConfig.PARRY_WINDOW_TICKS, stats.ParryWindowTicks);
        Assert.Equal(SimConfig.PARRY_COOLDOWN_TICKS, stats.ParryCooldownTicks);
    }

    [Fact]
    public void ClampedTickValues_FitTheByteCountersInPlayerState()
    {
        MatchConfig maxed = new()
        {
            DashCooldown = 999,
            RopeMissCooldown = 999,
            MortarReloadPerShell = 999,
            RespawnDelay = 999,
            CoyoteMax = 999,
            ParryWindow = 999,
            ParryCooldown = 999,
        };
        maxed.Clamp();
        PlayerStats stats = PlayerStats.Resolve(maxed);

        Assert.InRange(stats.DashCooldownTicks, 1, 255);
        Assert.InRange(stats.RopeMissCooldownTicks, 1, 255);
        Assert.InRange(stats.ReloadTicks, 1, 255);
        Assert.InRange(stats.CoyoteMaxTicks, 1, 255);
        Assert.InRange(maxed.RespawnDelayTicks, 1, 255);
        Assert.InRange(stats.ParryWindowTicks, 1, 255);
        Assert.InRange(stats.ParryCooldownTicks, 1, ushort.MaxValue);
    }
}
