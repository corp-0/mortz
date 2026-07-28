using System.Reflection;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Ui;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class MatchConfigTests
{
    [Fact]
    public void GeneratedMetadata_CoversEveryWritableRule_InDeclarationOrder()
    {
        IUiPropertyDescriptor[] descriptors = MatchConfigUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .ToArray();
        PropertyInfo[] writableRules = WritableRules().ToArray();

        Assert.Equal(
            writableRules.Select(property => property.Name),
            descriptors.Select(descriptor => descriptor.Name));
        foreach (IUiPropertyDescriptor descriptor in descriptors)
        {
            PropertyInfo property = Assert.Single(
                writableRules, candidate => candidate.Name == descriptor.Name);
            Assert.Equal(property.PropertyType, descriptor.ValueType);
        }
    }

    [Fact]
    public void GeneratedMetadata_BindsTypedAndUntypedValues()
    {
        MatchConfig config = new();
        IUiPropertyDescriptor gravity = MatchConfigUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(MatchConfig.Gravity));
        UiPropertyDescriptor<MatchConfig, float> typedGravity =
            Assert.IsType<UiPropertyDescriptor<MatchConfig, float>>(gravity);

        typedGravity.Set(config, 321f);
        Assert.Equal(321f, typedGravity.Get(config));
        gravity.SetValue(config, 654f);
        Assert.Equal(654f, gravity.GetValue(config));
        Assert.Throws<ArgumentException>(() => gravity.SetValue(config, 654));
        Assert.Throws<ArgumentException>(() => gravity.SetValue(new object(), 654f));

        IUiPropertyDescriptor winCondition = MatchConfigUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(MatchConfig.WinCondition));
        winCondition.SetValue(config, WinCondition.TEAM_KILLS);
        Assert.Equal(WinCondition.TEAM_KILLS, config.WinCondition);
    }

    [Fact]
    public void GeneratedMetadata_CarriesRenderHints()
    {
        IUiPropertyDescriptor[] descriptors = MatchConfigUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .ToArray();

        IUiPropertyDescriptor gravity = descriptors
            .Single(property => property.Name == nameof(MatchConfig.Gravity));
        Assert.Equal(100, gravity.Min);
        Assert.Equal(8000, gravity.Max);
        Assert.Equal(50, gravity.Step);

        // step not stated
        IUiPropertyDescriptor killTarget = descriptors
            .Single(property => property.Name == nameof(MatchConfig.KillTarget));
        Assert.Equal(1, killTarget.Min);
        Assert.Equal(999, killTarget.Max);
        Assert.Null(killTarget.Step);

        IUiPropertyDescriptor teams = descriptors
            .Single(property => property.Name == nameof(MatchConfig.Teams));
        Assert.Null(teams.Min);
        Assert.Null(teams.Max);
        Assert.Null(teams.Step);
    }

    /// <summary>Reflection so a new rule that nobody added to the codec fails
    /// here instead of silently arriving at its default on every client.</summary>
    [Fact]
    public void WireBlob_CarriesEveryRule()
    {
        MatchConfig expected = new();
        foreach (PropertyInfo property in WritableRules())
        {
            object current = property.GetValue(expected)!;
            object changed = current switch
            {
                float f => f + 0.01f,
                int i => i + 1,
                bool b => !b,
                WinCondition => WinCondition.TEAM_KILLS,
                SuicidePenalty => SuicidePenalty.REWARD_CLOSEST_ENEMY,
                _ => throw new InvalidOperationException($"Unhandled rule type {property.PropertyType}"),
            };
            property.SetValue(expected, changed);
        }
        expected.Clamp();

        MatchConfig actual = MatchConfig.FromBytes(expected.ToBytes());

        foreach (PropertyInfo property in WritableRules())
        {
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
    }

    private static IEnumerable<PropertyInfo> WritableRules() =>
        typeof(MatchConfig).GetProperties().Where(property => property.CanWrite);

    [Fact]
    public void WireBlob_RoundTrips()
    {
        MatchConfig sent = new()
        {
            Gravity = 750,
            MortarMaxAmmo = 8,
            GroundFriction = 0,
            SpawnImmunity = 2.25f,
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.Equal(750, got.Gravity);
        Assert.Equal(8, got.MortarMaxAmmo);
        Assert.Equal(0, got.GroundFriction);
        Assert.Equal(2.25f, got.SpawnImmunity);
        Assert.Equal(SimConfig.MAX_RUN_SPEED, got.MaxRunSpeed); // untouched fields keep defaults
    }

    [Fact]
    public void WireBlob_RejectsTrailingBytes()
    {
        byte[] valid = new MatchConfig().ToBytes();

        Assert.Throws<InvalidDataException>(() => MatchConfig.FromBytes([.. valid, 0]));
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
            SpawnImmunity = 999,
        };
        MatchConfig got = MatchConfig.FromBytes(hostile.ToBytes());

        Assert.Equal(100, got.Gravity); // NaN lands on the minimum
        Assert.Equal(128, got.MortarCarveRadius);
        Assert.Equal(250, got.MaxHealth);
        Assert.Equal(0.1f, got.MortarReloadPerShell);
        Assert.Equal(4, got.SpawnImmunity);
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
            SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.True(got.Teams);
        Assert.Equal(WinCondition.TEAM_KILLS, got.WinCondition);
        Assert.Equal(5, got.KillTarget);
        Assert.False(got.FriendlyFire);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, got.SuicidePenalty);

        MatchConfig hostile = new() { WinCondition = (WinCondition)99, KillTarget = 0 };
        got = MatchConfig.FromBytes(hostile.ToBytes());
        Assert.Equal(WinCondition.PLAYER_KILLS, got.WinCondition);
        Assert.Equal(1, got.KillTarget);
    }

    [Fact]
    public void TryApplyKey_SetsTypedValuesFromRawTomlTypes()
    {
        MatchConfig config = new();

        Assert.Equal(ConfigKeyResult.APPLIED, config.TryApplyKey("teams", true, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, config.TryApplyKey("win_condition", "team_kills", out _));
        Assert.Equal(ConfigKeyResult.APPLIED, config.TryApplyKey("kill_target", 30L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, config.TryApplyKey("gravity", 600L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, config.TryApplyKey("spawn_immunity", 2.5, out _));

        Assert.True(config.Teams);
        Assert.Equal(WinCondition.TEAM_KILLS, config.WinCondition);
        Assert.Equal(30, config.KillTarget);
        Assert.Equal(600, config.Gravity); // integers feed float rules
        Assert.Equal(2.5f, config.SpawnImmunity);
        Assert.Equal(150, config.SpawnImmunityTicks);
        Assert.True(config.FriendlyFire); // untouched fields keep defaults
    }

    [Fact]
    public void TryApplyKey_RejectsUnknownKeysAndWrongTypes()
    {
        MatchConfig config = new();

        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, config.TryApplyKey("lives", 3L, out string error));
        Assert.Contains("lives", error);
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, config.TryApplyKey("teams", "yes", out _));
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, config.TryApplyKey("win_condition", "most_flags", out error));
        Assert.Contains("player_kills", error); // the error lists the legal values
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, config.TryApplyKey("kill_target", 1.5, out _));
    }

    [Fact]
    public void DefaultResolvedStats_MatchTheSimConfigConsts()
    {
        PlayerStats stats = PlayerStats.Resolve(new MatchConfig());

        Assert.Equal(SimConfig.MAX_RUN_SPEED, stats.MaxRunSpeed);
        Assert.Equal(SimConfig.TOTAL_JUMPS, stats.TotalJumps);
        Assert.Equal(SimConfig.DASH_COOLDOWN_TICKS, stats.DashCooldownTicks);
        Assert.Equal(SimConfig.MORTAR_RELOAD_TICKS, stats.ReloadPerShellTicks);
        Assert.Equal(SimConfig.COYOTE_MAX_TICKS, stats.CoyoteMaxTicks);
        Assert.Equal(SimConfig.MAX_HEALTH, stats.MaxHealth);
        Assert.Equal(SimConfig.PARRY_WINDOW_TICKS, stats.ParryWindowTicks);
        Assert.Equal(SimConfig.PARRY_COOLDOWN_TICKS, stats.ParryCooldownTicks);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY, new MatchConfig().SpawnImmunity);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY_TICKS, new MatchConfig().SpawnImmunityTicks);
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
            SpawnImmunity = 999,
            CoyoteMax = 999,
            ParryWindow = 999,
            ParryCooldown = 999,
        };
        maxed.Clamp();
        PlayerStats stats = PlayerStats.Resolve(maxed);

        Assert.InRange(stats.DashCooldownTicks, 1, 255);
        Assert.InRange(stats.RopeMissCooldownTicks, 1, 255);
        Assert.InRange(stats.ReloadPerShellTicks, 1, 255);
        Assert.InRange(stats.CoyoteMaxTicks, 1, 255);
        Assert.InRange(maxed.RespawnDelayTicks, 1, 255);
        Assert.InRange(maxed.SpawnImmunityTicks, 1, 255);
        Assert.InRange(stats.ParryWindowTicks, 1, 255);
        Assert.InRange(stats.ParryCooldownTicks, 1, ushort.MaxValue);
    }
}
