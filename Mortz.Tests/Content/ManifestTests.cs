using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Xunit;

namespace Mortz.Tests.Content;

public class ManifestTests
{
    [Fact]
    public void ModeParsesRulesOverlayOverDefaults()
    {
        ContentReadResult<GameModeManifest> result = TomlModel.Read<GameModeManifest>("""
            format_version = 1
            name = "Team Deathmatch"
            description = "Two teams."
            identity = ["rules.teams", "rules.victory.target"]

            [rules]
            teams = true

            [rules.victory]
            type = "kills"
            target = 15
            """, "mode.toml");

        GameModeManifest manifest = Assert.IsType<GameModeManifest>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Team Deathmatch", manifest.Name);
        Assert.Equal("Two teams.", manifest.Description);
        Assert.Equal(["rules.teams", "rules.victory.target"], manifest.Identity);
        Assert.True(manifest.Rules.Teams);
        Assert.Equal(15,
            Assert.IsType<KillsVictoryRules>(manifest.Rules.Victory).Target);
        Assert.True(manifest.Rules.FriendlyFire);
    }

    [Fact]
    public void ModeIdentityUsesItsAuthoredConfigPaths()
    {
        GameModeManifest manifest = new()
        {
            FormatVersion = 1,
            Name = "Five Kill Teams",
            Identity = ["rules.teams", "rules.victory.type", "rules.victory.target"],
            Rules = new ModeRules
            {
                Teams = true,
                Victory = new KillsVictoryRules { Target = 5 },
            },
        };

        Assert.True(manifest.Matches(new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = true,
                Victory = new KillsVictoryRules { Target = 5 },
            },
        }));
        Assert.False(manifest.Matches(new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = true,
                Victory = new KillsVictoryRules { Target = 6 },
            },
        }));
    }

    [Fact]
    public void ModeWithoutRulesIsTheDefaultConfig()
    {
        ContentReadResult<GameModeManifest> result = TomlModel.Read<GameModeManifest>(
            "format_version = 1\nname = \"Vanilla\"\n", "mode.toml");

        GameModeManifest manifest = Assert.IsType<GameModeManifest>(result.Value);
        Assert.Equal(new ModeRules().ToBytes(), manifest.Rules.ToBytes());
        Assert.Equal(new Physics().ToBytes(), manifest.Physics.ToBytes());
        Assert.Equal(new Combat().ToBytes(), manifest.Combat.ToBytes());
    }

    [Fact]
    public void UnknownRuleKeyWarnsButModeLoads()
    {
        ContentReadResult<GameModeManifest> result = TomlModel.Read<GameModeManifest>("""
            format_version = 1
            name = "Typo"

            [rules]
            kil_target = 15
            """, "mode.toml");

        Assert.NotNull(result.Value);
        ContentDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticSeverity.WARNING, warning.Severity);
        Assert.Contains("rules.kil_target", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRuleValueRejectsTheMode()
    {
        ContentReadResult<GameModeManifest> result = TomlModel.Read<GameModeManifest>("""
            format_version = 1
            name = "Broken"

            [rules.victory]
            type = "most_flags"
            """, "mode.toml");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR &&
                          diagnostic.Message.Contains("rules.victory.type", StringComparison.Ordinal));
    }

    [Fact]
    public void TomlReadingDoesNotApplyBusinessValidation()
    {
        ContentReadResult<GameModeManifest> result = TomlModel.Read<GameModeManifest>("""
            format_version = 1
            name = "Greedy"

            [rules.victory]
            type = "kills"
            target = 5000
            """, "mode.toml");

        GameModeManifest manifest = Assert.IsType<GameModeManifest>(result.Value);
        Assert.Equal(5000,
            Assert.IsType<KillsVictoryRules>(manifest.Rules.Victory).Target);
    }

    [Fact]
    public void RulesetRoundTripsTheSelectedVictoryRule()
    {
        RulesetManifest expected = new()
        {
            Rules = new ModeRules
            {
                Victory = new KillLeadVictoryRules { Target = 7 },
            },
        };

        string text = TomlModel.Write(expected);
        ContentReadResult<RulesetManifest> result = TomlModel.Read<RulesetManifest>(text);

        RulesetManifest actual = Assert.IsType<RulesetManifest>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("[rules.victory]", text, StringComparison.Ordinal);
        Assert.Contains("type = \"kill_lead\"", text, StringComparison.Ordinal);
        Assert.Equal(7,
            Assert.IsType<KillLeadVictoryRules>(actual.Rules.Victory).Target);
    }

    [Fact]
    public void RulesetFileReadsPhysicsTable()
    {
        ContentReadResult<RulesetManifest> result = TomlModel.Read<RulesetManifest>("""
            [physics]
            gravity = 600
            rope_pull_accel = 4000
            """);

        RulesetManifest config = Assert.IsType<RulesetManifest>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(600, config.Physics.Gravity);
        Assert.Equal(4000, config.Physics.RopePullAccel);
    }

    [Fact]
    public void RulesetWarnsOnUnknownTopLevelKeys()
    {
        ContentReadResult<RulesetManifest> result = TomlModel.Read<RulesetManifest>(
            "name = \"stray\"\n\n[rules]\nteams = true\n");

        Assert.NotNull(result.Value);
        ContentDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticSeverity.WARNING, warning.Severity);
        Assert.Contains("name", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapManifestRequiresNameAndSuggestedPlayers()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>("name = \"Arena\"\n");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("suggested_players", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownMapKeyWarnsButLoads()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(
            "name = \"Arena\"\nsuggested_players = 4\nsugested_players = 8\n");

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Equal(4, manifest.SuggestedPlayers);
        ContentDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticSeverity.WARNING, warning.Severity);
        Assert.Contains("sugested_players", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizedMapRoundTripsEscapedNames()
    {
        MapManifest expected = new() { Name = "Gilles' \"Arena\"\nTwo", SuggestedPlayers = 8 };

        string text = TomlModel.Write(expected);
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(text);

        Assert.Equal(expected, result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            "name = \"Gilles' \\\"Arena\\\"\\nTwo\"\nsuggested_players = 8\n",
            text);
    }

    [Fact]
    public void NormalizedMapEscapesAllTomlControlCharactersAndPreservesUnicode()
    {
        MapManifest expected = new() { Name = "A\0B\vC\u001FD\u007F 🐛", SuggestedPlayers = 2 };

        string text = TomlModel.Write(expected);
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(text);

        Assert.Equal(expected.Name, Assert.IsType<MapManifest>(result.Value).Name);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("\\u0000", text, StringComparison.Ordinal);
        Assert.Contains("\\u000B", text, StringComparison.Ordinal);
        Assert.Contains("\\u001F", text, StringComparison.Ordinal);
        Assert.Contains("\\u007F", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("org.gilles.something")]
    [InlineData("io.github.some-project")]
    public void PackAcceptsReverseDomainId(string id)
    {
        ContentReadResult<ContentPackManifest> result = TomlModel.Read<ContentPackManifest>(
            $"id = \"{id}\"\nname = \"Pack\"\nversion = \"1\"\n");

        Assert.NotNull(result.Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SpawnPointsParseInAuthoredOrderAndRoundTrip()
    {
        const string TEXT = """
            name = "Arena"
            suggested_players = 2

            [[spawn_points]]
            x = 100
            y = 250

            [[spawn_points]]
            x = 300
            y = 250
            """;

        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(TEXT);

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Equal([new MapSpawnPoint(100, 250), new MapSpawnPoint(300, 250)],
            manifest.SpawnPoints);
        string normalized = TomlModel.Write(manifest);
        Assert.Equal(manifest.SpawnPoints,
            Assert.IsType<MapManifest>(TomlModel.Read<MapManifest>(normalized).Value).SpawnPoints);
    }

    [Fact]
    public void SpawnPointTeamOwnershipIsOptionalAndRoundTrips()
    {
        const string TEXT = """
            name = "Arena"
            suggested_players = 2

            [[spawn_points]]
            x = 100
            y = 250
            team = "blue"

            [[spawn_points]]
            x = 300
            y = 250
            """;

        MapManifest manifest = Assert.IsType<MapManifest>(
            TomlModel.Read<MapManifest>(TEXT).Value);

        Assert.Equal(Team.BLUE, manifest.SpawnPoints[0].Team);
        Assert.Null(manifest.SpawnPoints[1].Team);
        MapManifest roundTrip = Assert.IsType<MapManifest>(
            TomlModel.Read<MapManifest>(TomlModel.Write(manifest)).Value);
        Assert.Equal(manifest.SpawnPoints, roundTrip.SpawnPoints);
    }

    [Fact]
    public void MalformedSpawnIdentifiesEntryAndField()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>("""
            name = "Arena"
            suggested_players = 2

            [[spawn_points]]
            x = 100
            """);

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("spawn_points[0]", StringComparison.Ordinal) &&
                          diagnostic.Message.Contains("'y'", StringComparison.Ordinal));
    }

}
