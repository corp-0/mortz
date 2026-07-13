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
        };
        maxed.Clamp();
        PlayerStats stats = PlayerStats.Resolve(maxed);

        Assert.InRange(stats.DashCooldownTicks, 1, 255);
        Assert.InRange(stats.RopeMissCooldownTicks, 1, 255);
        Assert.InRange(stats.ReloadTicks, 1, 255);
        Assert.InRange(stats.CoyoteMaxTicks, 1, 255);
        Assert.InRange(maxed.RespawnDelayTicks, 1, 255);
    }
}
