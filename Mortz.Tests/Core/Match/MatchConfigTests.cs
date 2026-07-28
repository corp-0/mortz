using System.Reflection;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Ui;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class MatchConfigTests
{
    [Fact]
    public void GeneratedMetadata_CoversEveryWritableProperty_InDeclarationOrder()
    {
        AssertMetadataCoversWritableProperties(typeof(ModeRules), ModeRulesUiMetadata.Categories);
        AssertMetadataCoversWritableProperties(typeof(Physics), PhysicsUiMetadata.Categories);
    }

    private static void AssertMetadataCoversWritableProperties(
        Type owner, IReadOnlyList<UiCategoryDescriptor> categories)
    {
        IUiPropertyDescriptor[] descriptors = categories
            .SelectMany(category => category.Properties)
            .ToArray();
        PropertyInfo[] writableProperties = WritableProperties(owner).ToArray();

        Assert.Equal(
            writableProperties.Select(property => property.Name),
            descriptors.Select(descriptor => descriptor.Name));
        foreach (IUiPropertyDescriptor descriptor in descriptors)
        {
            PropertyInfo property = Assert.Single(
                writableProperties, candidate => candidate.Name == descriptor.Name);
            Assert.Equal(property.PropertyType, descriptor.ValueType);
        }
    }

    [Fact]
    public void GeneratedMetadata_BindsTypedAndUntypedValues()
    {
        Physics physics = new();
        IUiPropertyDescriptor gravity = PhysicsUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(Physics.Gravity));
        UiPropertyDescriptor<Physics, float> typedGravity =
            Assert.IsType<UiPropertyDescriptor<Physics, float>>(gravity);

        typedGravity.Set(physics, 321f);
        Assert.Equal(321f, typedGravity.Get(physics));
        gravity.SetValue(physics, 654f);
        Assert.Equal(654f, gravity.GetValue(physics));
        Assert.Throws<ArgumentException>(() => gravity.SetValue(physics, 654));
        Assert.Throws<ArgumentException>(() => gravity.SetValue(new object(), 654f));

        ModeRules rules = new();
        IUiPropertyDescriptor winCondition = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.WinCondition));
        rules.WinCondition = (WinCondition)99;
        winCondition.SetValue(rules, WinCondition.KILLS);
        Assert.Equal(WinCondition.KILLS, rules.WinCondition);
    }

    [Fact]
    public void GeneratedMetadata_CarriesRenderHints()
    {
        IUiPropertyDescriptor gravity = PhysicsUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(Physics.Gravity));
        Assert.Equal(100, gravity.Min);
        Assert.Equal(8000, gravity.Max);
        Assert.Equal(50, gravity.Step);

        IUiPropertyDescriptor[] ruleDescriptors = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .ToArray();

        // step not stated
        IUiPropertyDescriptor killTarget = ruleDescriptors
            .Single(property => property.Name == nameof(ModeRules.KillTarget));
        Assert.Equal(1, killTarget.Min);
        Assert.Equal(999, killTarget.Max);
        Assert.Null(killTarget.Step);

        IUiPropertyDescriptor killLeadTarget = ruleDescriptors
            .Single(property => property.Name == nameof(ModeRules.KillLeadTarget));
        Assert.Equal(1, killLeadTarget.Min);
        Assert.Equal(999, killLeadTarget.Max);
        Assert.Null(killLeadTarget.Step);

        IUiPropertyDescriptor teams = ruleDescriptors
            .Single(property => property.Name == nameof(ModeRules.Teams));
        Assert.Null(teams.Min);
        Assert.Null(teams.Max);
        Assert.Null(teams.Step);
    }

    [Fact]
    public void GeneratedMetadata_EvaluatesConditionalVisibility()
    {
        IUiPropertyDescriptor killTarget = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.KillTarget));
        IUiPropertyDescriptor killLeadTarget = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.KillLeadTarget));
        IUiPropertyDescriptor friendlyFire = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.FriendlyFire));
        ModeRules rules = new();

        Assert.True(killTarget.IsVisible(rules));
        Assert.False(killLeadTarget.IsVisible(rules));
        Assert.False(friendlyFire.IsVisible(rules));

        rules.WinCondition = WinCondition.KILL_LEAD;

        Assert.False(killTarget.IsVisible(rules));
        Assert.True(killLeadTarget.IsVisible(rules));

        rules.WinCondition = (WinCondition)99;
        rules.Teams = true;

        Assert.False(killTarget.IsVisible(rules));
        Assert.False(killLeadTarget.IsVisible(rules));
        Assert.True(friendlyFire.IsVisible(rules));
    }

    [Fact]
    public void WireBlob_CarriesEveryConfigProperty()
    {
        MatchConfig expected = new();
        ChangeWritableProperties(expected.Rules);
        ChangeWritableProperties(expected.Physics);
        expected.Clamp();

        MatchConfig actual = MatchConfig.FromBytes(expected.ToBytes());

        AssertWritablePropertiesEqual(expected.Rules, actual.Rules);
        AssertWritablePropertiesEqual(expected.Physics, actual.Physics);
    }

    private static IEnumerable<PropertyInfo> WritableProperties(Type owner) =>
        owner.GetProperties().Where(property => property.CanWrite);

    private static void ChangeWritableProperties<T>(T instance)
    {
        foreach (PropertyInfo property in WritableProperties(typeof(T)))
        {
            object current = property.GetValue(instance)!;
            object changed = current switch
            {
                float value => value + 0.01f,
                int value => value + 1,
                bool value => !value,
                WinCondition => (WinCondition)99,
                SuicidePenalty => SuicidePenalty.REWARD_CLOSEST_ENEMY,
                _ => throw new InvalidOperationException(
                    $"Unhandled config type {property.PropertyType}"),
            };
            property.SetValue(instance, changed);
        }
    }

    private static void AssertWritablePropertiesEqual<T>(T expected, T actual)
    {
        foreach (PropertyInfo property in WritableProperties(typeof(T)))
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
    }

    [Fact]
    public void WireBlob_RoundTrips()
    {
        MatchConfig sent = new()
        {
            Physics = new Physics
            {
                Gravity = 750,
                MortarMaxAmmo = 8,
                GroundFriction = 0,
                SpawnImmunity = 2.25f,
            },
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.Equal(750, got.Physics.Gravity);
        Assert.Equal(8, got.Physics.MortarMaxAmmo);
        Assert.Equal(0, got.Physics.GroundFriction);
        Assert.Equal(2.25f, got.Physics.SpawnImmunity);
        Assert.Equal(SimConfig.MAX_RUN_SPEED, got.Physics.MaxRunSpeed);
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
            Physics = new Physics
            {
                Gravity = float.NaN,
                MortarCarveRadius = 100000,
                MaxHealth = 9999,
                MortarReloadPerShell = -3,
                SpawnImmunity = 999,
            },
        };
        MatchConfig got = MatchConfig.FromBytes(hostile.ToBytes());

        Assert.Equal(100, got.Physics.Gravity);
        Assert.Equal(128, got.Physics.MortarCarveRadius);
        Assert.Equal(250, got.Physics.MaxHealth);
        Assert.Equal(0.1f, got.Physics.MortarReloadPerShell);
        Assert.Equal(4, got.Physics.SpawnImmunity);
    }

    [Fact]
    public void ModeFields_RoundTripTheWire_AndClampHostileValues()
    {
        MatchConfig sent = new()
        {
            Rules = new ModeRules
            {
                Teams = true,
                WinCondition = WinCondition.KILLS,
                KillTarget = 5,
                KillLeadTarget = 4,
                FriendlyFire = false,
                SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
            },
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.True(got.Rules.Teams);
        Assert.Equal(WinCondition.KILLS, got.Rules.WinCondition);
        Assert.Equal(5, got.Rules.KillTarget);
        Assert.Equal(4, got.Rules.KillLeadTarget);
        Assert.False(got.Rules.FriendlyFire);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, got.Rules.SuicidePenalty);

        MatchConfig hostile = new()
        {
            Rules = new ModeRules
            {
                WinCondition = (WinCondition)99,
                KillTarget = 0,
                KillLeadTarget = 1000,
            },
        };
        got = MatchConfig.FromBytes(hostile.ToBytes());
        Assert.Equal(WinCondition.KILLS, got.Rules.WinCondition);
        Assert.Equal(1, got.Rules.KillTarget);
        Assert.Equal(999, got.Rules.KillLeadTarget);
    }

    [Fact]
    public void TryApplyKey_SetsTypedValuesFromRawTomlTypes()
    {
        ModeRules rules = new();
        Physics physics = new();

        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("teams", true, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("win_condition", "kills", out _));
        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("kill_target", 30L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("kill_lead_target", 4L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, physics.TryApplyKey("gravity", 600L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, physics.TryApplyKey("spawn_immunity", 2.5, out _));

        Assert.True(rules.Teams);
        Assert.Equal(WinCondition.KILLS, rules.WinCondition);
        Assert.Equal(30, rules.KillTarget);
        Assert.Equal(4, rules.KillLeadTarget);
        Assert.Equal(600, physics.Gravity);
        Assert.Equal(2.5f, physics.SpawnImmunity);
        Assert.Equal(150, physics.SpawnImmunityTicks);
        Assert.True(rules.FriendlyFire);
    }

    [Fact]
    public void TryApplyKey_RejectsUnknownKeysAndWrongTypes()
    {
        ModeRules rules = new();

        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, rules.TryApplyKey("lives", 3L, out string error));
        Assert.Contains("lives", error);
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, rules.TryApplyKey("teams", "yes", out _));
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, rules.TryApplyKey("win_condition", "most_flags", out error));
        Assert.Contains("kills", error); // the error lists the legal values
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, rules.TryApplyKey("kill_target", 1.5, out _));
        Assert.Equal(ConfigKeyResult.INVALID_VALUE,
            rules.TryApplyKey("kill_lead_target", 1.5, out _));

        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, rules.TryApplyKey("gravity", 600L, out _));
        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, new Physics().TryApplyKey("teams", true, out _));
    }

    [Fact]
    public void DefaultResolvedStats_MatchTheSimConfigConsts()
    {
        PlayerStats stats = PlayerStats.Resolve(new Physics());

        Assert.Equal(SimConfig.MAX_RUN_SPEED, stats.MaxRunSpeed);
        Assert.Equal(SimConfig.TOTAL_JUMPS, stats.TotalJumps);
        Assert.Equal(SimConfig.DASH_COOLDOWN_TICKS, stats.DashCooldownTicks);
        Assert.Equal(SimConfig.MORTAR_RELOAD_TICKS, stats.ReloadPerShellTicks);
        Assert.Equal(SimConfig.COYOTE_MAX_TICKS, stats.CoyoteMaxTicks);
        Assert.Equal(SimConfig.MAX_HEALTH, stats.MaxHealth);
        Assert.Equal(SimConfig.PARRY_WINDOW_TICKS, stats.ParryWindowTicks);
        Assert.Equal(SimConfig.PARRY_COOLDOWN_TICKS, stats.ParryCooldownTicks);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY, new Physics().SpawnImmunity);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY_TICKS, new Physics().SpawnImmunityTicks);
    }

    [Fact]
    public void ClampedTickValues_FitTheByteCountersInPlayerState()
    {
        Physics maxed = new()
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
