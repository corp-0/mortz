using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class ConfigurationVariantTests
{
    [Fact]
    public void ActiveVariantsSurviveAllConfigurationProjections()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                EndCondition = new ScoreLeadRules { Target = 7 },
                Respawn = new FixedRespawnRules { Delay = 3.25f },
                SpawnImmunity = 0.75f,
                SpectateDuringRespawn = false,
            },
        };
        MatchConfigSnapshot snapshot = config.ToSnapshot();
        MatchConfig copy = snapshot.ToMutable();
        Assert.NotSame(config.Rules.Objective, copy.Rules.Objective);
        Assert.NotSame(config.Rules.Respawn, copy.Rules.Respawn);
        Assert.Equal(snapshot, copy.ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(config.ToBytes()).ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(snapshot.ToBytes()).ToSnapshot());

        string toml = TomlModel.Write(new RulesetManifest
        {
            Rules = config.Rules,
            Physics = config.Physics,
            Combat = config.Combat,
        });
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(toml);
        Assert.Empty(read.Diagnostics);
        Assert.Equal(snapshot, read.Value!.ToMatchConfigSnapshot());
        Assert.Contains("[rules.respawn]", toml);
        Assert.DoesNotContain("respawn_delay", toml);
        ((FixedRespawnRules)copy.Rules.Respawn).Delay = 8;
        Assert.Equal(3.25f, Assert.IsType<FixedRespawnRulesSnapshot>(snapshot.Rules.Respawn).Delay);
    }

    [Theory]
    [InlineData("end_condition", "tickets")]
    [InlineData("objective", "deadlock")]
    [InlineData("respawn", "escalating")]
    [InlineData("respawn", "disabled")]
    public void UnimplementedVariantsAreRejectedByToml(string family, string variant)
    {
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>($"[rules.{family}]\ntype = \"{variant}\"\n");
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, error => error.Severity == ContentDiagnosticSeverity.ERROR &&
            error.Message.Contains(variant, StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigWireContainsOnlyDiscriminatorsAndSelectedSettings()
    {
        Assert.Equal(Convert.FromHexString("046E6F6E6500000000"),
            ObjectiveRulesCodec.ToBytes(new NoObjectiveRules()));
        Assert.Equal(Convert.FromHexString("0566697865640400000000005040"),
            RespawnRulesCodec.ToBytes(new FixedRespawnRules { Delay = 3.25f }));
        Assert.Equal(Convert.FromHexString("0A73636F72655F6C6561640400000007000000"),
            EndConditionRulesCodec.ToBytes(new ScoreLeadRules { Target = 7 }));
        Assert.Empty(NoObjectiveRulesCodec.ToBytes(new NoObjectiveRules()));
        Assert.Empty(ObjectiveRulesMetadata.For(new NoObjectiveRules()).Categories);
        Assert.Equal(["Delay"], RespawnRulesMetadata.For(new FixedRespawnRules()).Categories
            .SelectMany(category => category.Properties).Select(property => property.Name));
        Assert.DoesNotContain(ModeRulesUiMetadata.Categories.SelectMany(category => category.Properties),
            property => property.Name is "Delay" or "RespawnDelay");
    }

    [Fact]
    public void UnknownWireVariantsAndMalformedSegmentsFailClearly()
    {
        byte[] unknown = Convert.FromHexString("046F6F707300000000");
        Assert.Contains("Unknown", Assert.Throws<InvalidDataException>(() =>
            ObjectiveRulesCodec.FromBytes(unknown)).Message);
        Assert.Throws<InvalidDataException>(() => RespawnRulesCodec.FromBytes(unknown));
        Assert.Throws<InvalidDataException>(() => EndConditionRulesCodec.FromBytes(unknown));
        Assert.Throws<InvalidDataException>(() =>
            ObjectiveRulesCodec.FromBytes(Convert.FromHexString("046E6F6E6501000000")));
        Assert.Throws<InvalidDataException>(() =>
            NoObjectiveRulesCodec.FromBytes([0]));
        byte[] valid = new MatchConfig().ToBytes();
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(valid[..^1]));
    }

    [Fact]
    public void InvalidCompositionIsRejectedByContentWireAndDirectMatchConstruction()
    {
        MatchConfig invalid = new()
        {
            Rules = new ModeRules { EndCondition = new TimeLimitRules() },
        };
        Assert.Contains(ModeComposition.Errors(invalid.Rules), error =>
            error.Contains("requires rules.evaluation = 'tick'", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(invalid.ToBytes()));
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>("[rules.end_condition]\ntype = \"time_limit\"\nseconds = 60\n");
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, error => error.Message.Contains("evaluation", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new MatchRuntime(
            new TerrainMask(8, 8, (_, _) => false, (_, _) => false), invalid, 0, new MatchStateKeys(1)));
        invalid.Rules.Evaluation = EvaluationTiming.TICK;
        Assert.Empty(ModeComposition.Errors(invalid.Rules));
    }

    [Fact]
    public void InvalidVariantsCannotHideBehindTheirSnapshotProjection()
    {
        MatchConfig config = new() { Rules = new ModeRules { Respawn = new UnknownRespawnRules() } };
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains("rules.respawn"));
        Assert.Throws<ArgumentException>(() => new SimWorld(new TerrainMask(8, 8, (_, _) => false, (_, _) => false), config));
        config.Rules.Respawn = null!;
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains("null"));
    }

    [Fact]
    public void DelayTuningPreservesModeIdentityAndProducesASettingsDelta()
    {
        GameModeManifest mode = new() { Name = "Default", FormatVersion = 1 };
        MatchConfig before = mode.ToMatchConfigSnapshot().ToMutable();
        MatchConfig after = before.ToSnapshot().ToMutable();
        ((FixedRespawnRules)after.Rules.Respawn).Delay = 9.5f;
        Assert.True(mode.Matches(after));
        Assert.Contains("rules.objective.type", mode.Identity);
        Assert.Contains("rules.respawn.type", mode.Identity);
        LobbySettingDelta delta = Assert.Single(LobbySettingsDiff.Between(before, after));
        Assert.Equal(new LobbySettingDelta("Respawn Delay", "2", "9.5"), delta);
    }

    public class UnknownRespawnRules : RespawnRules
    {
        public override RespawnRulesSnapshot ToSnapshot() => new FixedRespawnRulesSnapshot(1);
    }
}
