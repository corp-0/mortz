using System.Reflection;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Ui;
using Mortz.Server.Settings;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Tests.Core.Match;

public class MatchConfigTests
{
    [Fact]
    public void GeneratedMetadata_CoversEveryWritableProperty_InDeclarationOrder()
    {
        AssertMetadataCoversWritableProperties(typeof(ModeRules), ModeRulesUiMetadata.Categories);
        AssertMetadataCoversWritableProperties(
            typeof(KillsVictoryRules), KillsVictoryRulesUiMetadata.Categories);
        AssertMetadataCoversWritableProperties(
            typeof(KillLeadVictoryRules), KillLeadVictoryRulesUiMetadata.Categories);
        AssertMetadataCoversWritableProperties(typeof(Physics), PhysicsUiMetadata.Categories);
        AssertMetadataCoversWritableProperties(typeof(Combat), CombatUiMetadata.Categories);
    }

    private static void AssertMetadataCoversWritableProperties(
        Type owner, IReadOnlyList<UiCategoryDescriptor> categories)
    {
        IUiPropertyDescriptor[] descriptors = categories
            .SelectMany(category => category.Properties)
            .ToArray();
        PropertyInfo[] writableProperties = WritableProperties(owner)
            .Where(property => property.GetCustomAttribute<UiPropertyAttribute>() != null)
            .ToArray();

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

        KillsVictoryRules rules = new();
        IUiPropertyDescriptor target = KillsVictoryRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(KillsVictoryRules.Target));
        target.SetValue(rules, 42);
        Assert.Equal(42, rules.Target);
    }

    [Fact]
    public void GeneratedMetadata_CarriesRenderHints()
    {
        IUiPropertyDescriptor gravity = PhysicsUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(Physics.Gravity));
        Assert.Equal(-8000, gravity.Min);
        Assert.Equal(8000, gravity.Max);
        Assert.Equal(50, gravity.Step);

        IUiPropertyDescriptor[] ruleDescriptors = KillsVictoryRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .ToArray();

        // No step authored, so the SpinBox keeps its scene default.
        IUiPropertyDescriptor killTarget = ruleDescriptors
            .Single(property => property.Name == nameof(KillsVictoryRules.Target));
        Assert.Equal(1, killTarget.Min);
        Assert.Equal(999, killTarget.Max);
        Assert.Null(killTarget.Step);

        IUiPropertyDescriptor killLeadTarget = KillLeadVictoryRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(KillLeadVictoryRules.Target));
        Assert.Equal(1, killLeadTarget.Min);
        Assert.Equal(999, killLeadTarget.Max);
        Assert.Null(killLeadTarget.Step);

        IUiPropertyDescriptor teams = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.Teams));
        Assert.Null(teams.Min);
        Assert.Null(teams.Max);
        Assert.Null(teams.Step);
    }

    [Fact]
    public void VictoryMetadata_DeclaresEverySelectableRuleType()
    {
        Assert.Collection(
            VictoryRulesMetadata.Variants,
            kills =>
            {
                Assert.Equal("kills", kills.Id);
                Assert.Equal("Kills", kills.DisplayName);
                Assert.IsType<KillsVictoryRules>(kills.CreateDefault());
            },
            lead =>
            {
                Assert.Equal("kill_lead", lead.Id);
                Assert.Equal("Kill Lead", lead.DisplayName);
                Assert.IsType<KillLeadVictoryRules>(lead.CreateDefault());
            });
    }

    [Fact]
    public void GeneratedMetadata_EvaluatesConditionalVisibility()
    {
        IUiPropertyDescriptor friendlyFire = ModeRulesUiMetadata.Categories
            .SelectMany(category => category.Properties)
            .Single(property => property.Name == nameof(ModeRules.FriendlyFire));
        ModeRules rules = new();

        Assert.False(friendlyFire.IsVisible(rules));
        rules.Teams = true;
        Assert.True(friendlyFire.IsVisible(rules));
    }

    [Fact]
    public void WireBlob_CarriesEveryConfigProperty()
    {
        MatchConfig expected = new();
        ChangeWritableProperties(expected.Rules);
        ChangeWritableProperties(expected.Physics);
        ChangeWritableProperties(expected.Combat);
        expected.Clamp();

        MatchConfig actual = MatchConfig.FromBytes(expected.ToBytes());

        AssertWritablePropertiesEqual(expected.Rules, actual.Rules);
        AssertWritablePropertiesEqual(expected.Physics, actual.Physics);
        AssertWritablePropertiesEqual(expected.Combat, actual.Combat);
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
                SuicidePenalty => SuicidePenalty.REWARD_CLOSEST_ENEMY,
                VictoryRules => new KillLeadVictoryRules { Target = 4 },
                _ => throw new InvalidOperationException(
                    $"Unhandled config type {property.PropertyType}"),
            };
            property.SetValue(instance, changed);
        }
    }

    private static void AssertWritablePropertiesEqual<T>(T expected, T actual)
    {
        foreach (PropertyInfo property in WritableProperties(typeof(T)))
        {
            object? expectedValue = property.GetValue(expected);
            object? actualValue = property.GetValue(actual);
            if (expectedValue is VictoryRules expectedVictory &&
                actualValue is VictoryRules actualVictory)
            {
                Assert.Equal(expectedVictory.GetType(), actualVictory.GetType());
                Assert.Equal(
                    ((KillLeadVictoryRules)expectedVictory).Target,
                    ((KillLeadVictoryRules)actualVictory).Target);
                continue;
            }
            Assert.Equal(expectedValue, actualValue);
        }
    }

    [Fact]
    public void WireBlob_RoundTrips()
    {
        MatchConfig sent = new()
        {
            Rules = new ModeRules { SpawnImmunity = 2.25f },
            Physics = new Physics
            {
                Gravity = 750,
                GroundFriction = 0,
            },
            Combat = new Combat { MortarMaxAmmo = 8 },
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.Equal(750, got.Physics.Gravity);
        Assert.Equal(8, got.Combat.MortarMaxAmmo);
        Assert.Equal(0, got.Physics.GroundFriction);
        Assert.Equal(2.25f, got.Rules.SpawnImmunity);
        Assert.Equal(SimConfig.MAX_RUN_SPEED, got.Physics.MaxRunSpeed);
    }

    [Fact]
    public void SnapshotEqualityIncludesVictorySubtypeAndValues()
    {
        MatchConfigSnapshot kills = new MatchConfig
        {
            Rules = new ModeRules { Victory = new KillsVictoryRules { Target = 5 } },
        }.ToSnapshot();
        MatchConfigSnapshot same = new MatchConfig
        {
            Rules = new ModeRules { Victory = new KillsVictoryRules { Target = 5 } },
        }.ToSnapshot();
        MatchConfigSnapshot differentValue = new MatchConfig
        {
            Rules = new ModeRules { Victory = new KillsVictoryRules { Target = 6 } },
        }.ToSnapshot();
        MatchConfigSnapshot differentSubtype = new MatchConfig
        {
            Rules = new ModeRules { Victory = new KillLeadVictoryRules { Target = 5 } },
        }.ToSnapshot();

        Assert.Equal(kills, same);
        Assert.NotEqual(kills, differentValue);
        Assert.NotEqual(kills, differentSubtype);
    }

    [Fact]
    public void SnapshotCopySharesNoMutableNestedObjects()
    {
        MatchConfig original = new()
        {
            Rules = new ModeRules { Victory = new KillLeadVictoryRules { Target = 7 } },
        };

        MatchConfig copy = original.ToSnapshot().ToMutable();

        Assert.NotSame(original, copy);
        Assert.NotSame(original.Rules, copy.Rules);
        Assert.NotSame(original.Rules.Victory, copy.Rules.Victory);
        Assert.NotSame(original.Physics, copy.Physics);
        Assert.NotSame(original.Combat, copy.Combat);
        Assert.Equal(original.ToSnapshot(), copy.ToSnapshot());
    }

    [Fact]
    public void SnapshotWireProjectionMatchesTheExistingGoldenLayout()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Teams = true,
                Victory = new KillLeadVictoryRules { Target = 17 },
                FriendlyFire = false,
                RespawnDelay = 1.25f,
            },
            Physics = new Physics
            {
                Gravity = 777,
                TotalJumps = 3,
            },
            Combat = new Combat
            {
                MortarSpeed = 1234,
                MortarDamage = 67,
            },
        };
        byte[] expected = Convert.FromHexString(
            "220000000C000000010000010000A03F0000E03F096B696C6C5F6C65616404000000110000005C00000000008C43000016450000E144000096440040424400006144030000000000C8430000C84300002043000002440000BE434C37893D3108AC3CCDCC4C3E000016441F852B3F0080A2440000024400401C45000016430000803E0000803F3800000000409A440000003F000048440000614430000000050000000000A03F00000C429A99193F0000204164000000430000000000003F23000000");

        Assert.Equal(expected, config.ToBytes());
        Assert.Equal(expected, config.ToSnapshot().ToBytes());
    }

    [Fact]
    public void LobbyDeltasCoverEveryEditableValue()
    {
        MatchConfig before = new();
        MatchConfig after = before.ToSnapshot().ToMutable();
        ChangeWritableProperties(after.Rules);
        ChangeWritableProperties(after.Physics);
        ChangeWritableProperties(after.Combat);
        int expected = ModeRulesUiMetadata.Categories.Sum(category => category.Properties.Count) +
                       PhysicsUiMetadata.Categories.Sum(category => category.Properties.Count) +
                       CombatUiMetadata.Categories.Sum(category => category.Properties.Count) + 1;

        LobbySettingDelta[] deltas = LobbySettingsDiff.Between(
            before.ToSnapshot(),
            after.ToSnapshot());

        Assert.Equal(expected, deltas.Length);
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
            Rules = new ModeRules { SpawnImmunity = 999 },
            Physics = new Physics { Gravity = float.NaN },
            Combat = new Combat
            {
                MortarCarveRadius = 100000,
                MaxHealth = 9999,
                MortarReloadPerShell = -3,
            },
        };
        MatchConfig got = MatchConfig.FromBytes(hostile.ToBytes());

        Assert.Equal(-8000, got.Physics.Gravity);
        Assert.Equal(128, got.Combat.MortarCarveRadius);
        Assert.Equal(250, got.Combat.MaxHealth);
        Assert.Equal(0.1f, got.Combat.MortarReloadPerShell);
        Assert.Equal(4, got.Rules.SpawnImmunity);
    }

    [Fact]
    public void ModeFields_RoundTripTheWire_AndClampHostileValues()
    {
        MatchConfig sent = new()
        {
            Rules = new ModeRules
            {
                Teams = true,
                Victory = new KillsVictoryRules { Target = 5 },
                FriendlyFire = false,
                SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
            },
        };
        MatchConfig got = MatchConfig.FromBytes(sent.ToBytes());

        Assert.True(got.Rules.Teams);
        Assert.Equal(5, Assert.IsType<KillsVictoryRules>(got.Rules.Victory).Target);
        Assert.False(got.Rules.FriendlyFire);
        Assert.Equal(SuicidePenalty.REWARD_CLOSEST_ENEMY, got.Rules.SuicidePenalty);

        MatchConfig hostile = new()
        {
            Rules = new ModeRules
            {
                Victory = new KillLeadVictoryRules { Target = 1000 },
            },
        };
        got = MatchConfig.FromBytes(hostile.ToBytes());
        Assert.Equal(999, Assert.IsType<KillLeadVictoryRules>(got.Rules.Victory).Target);
    }

    [Fact]
    public void Clamp_IncludesTheSelectedVictoryRules()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Victory = new KillsVictoryRules { Target = 1000 },
            },
        };

        config.Clamp();

        Assert.Equal(999, Assert.IsType<KillsVictoryRules>(config.Rules.Victory).Target);
    }

    [Fact]
    public void TryApplyKey_SetsTypedValuesFromRawTomlTypes()
    {
        ModeRules rules = new();
        KillsVictoryRules kills = new();
        KillLeadVictoryRules lead = new();
        Physics physics = new();

        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("teams", true, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, kills.TryApplyKey("target", 30L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, lead.TryApplyKey("target", 4L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, physics.TryApplyKey("gravity", 600L, out _));
        Assert.Equal(ConfigKeyResult.APPLIED, rules.TryApplyKey("spawn_immunity", 2.5, out _));

        Assert.True(rules.Teams);
        Assert.Equal(30, kills.Target);
        Assert.Equal(4, lead.Target);
        Assert.Equal(600, physics.Gravity);
        Assert.Equal(2.5f, rules.SpawnImmunity);
        Assert.Equal(150, rules.SpawnImmunityTicks);
        Assert.True(rules.FriendlyFire);
    }

    [Fact]
    public void TryApplyKey_RejectsUnknownKeysAndWrongTypes()
    {
        ModeRules rules = new();
        KillsVictoryRules kills = new();

        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, rules.TryApplyKey("lives", 3L, out string error));
        Assert.Contains("lives", error);
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, rules.TryApplyKey("teams", "yes", out _));
        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY,
            rules.TryApplyKey("win_condition", "most_flags", out error));
        Assert.Contains("win_condition", error);
        Assert.Equal(ConfigKeyResult.INVALID_VALUE, kills.TryApplyKey("target", 1.5, out _));

        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, rules.TryApplyKey("gravity", 600L, out _));
        Assert.Equal(ConfigKeyResult.UNKNOWN_KEY, new Physics().TryApplyKey("teams", true, out _));
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
        Assert.Equal(SimConfig.SPAWN_IMMUNITY, new ModeRules().SpawnImmunity);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY_TICKS, new ModeRules().SpawnImmunityTicks);
    }

    [Fact]
    public void ClampedTickValues_FitTheirPlayerStateCounters()
    {
        MatchConfig maxed = new()
        {
            Rules = new ModeRules
            {
                RespawnDelay = 999,
                SpawnImmunity = 999,
            },
            Physics = new Physics
            {
                DashCooldown = 999,
                RopeMissCooldown = 999,
                CoyoteMax = 999,
            },
            Combat = new Combat
            {
                MortarReloadPerShell = 999,
                ParryWindow = 999,
                ParryCooldown = 999,
            },
        };
        maxed.Clamp();
        PlayerStats stats = PlayerStats.Resolve(maxed);

        Assert.InRange(stats.DashCooldownTicks, 1, 255);
        Assert.InRange(stats.RopeMissCooldownTicks, 1, 255);
        Assert.InRange(stats.ReloadPerShellTicks, 1, 255);
        Assert.InRange(stats.CoyoteMaxTicks, 1, 255);
        Assert.InRange(maxed.Rules.RespawnDelayTicks, 1, ushort.MaxValue);
        Assert.InRange(maxed.Rules.SpawnImmunityTicks, 1, 255);
        Assert.InRange(stats.ParryWindowTicks, 1, 255);
        Assert.InRange(stats.ParryCooldownTicks, 1, ushort.MaxValue);
    }
}
