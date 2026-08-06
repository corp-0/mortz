using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Content;

public class TomlModelTests
{
    internal enum Difficulty
    {
        EASY,
        VERY_HARD,
    }

    internal sealed record Engine(string Kind, float Boost = 1.5f);

    internal sealed record Wave(int Count, string Label = "");

    [TomlModel]
    internal sealed record Fixture
    {
        public required int FormatVersion { get; init; }
        public required string Name { get; init; }
        public Wave[] Waves { get; init; } = [];
        [TomlName("max_speed")] public double Speed { get; init; } = 50;
        public bool Hardcore { get; init; }
        public Difficulty Difficulty { get; init; } = Difficulty.EASY;
        public string[]? Tags { get; init; }
        public Engine? Engine { get; init; }
    }

    [Fact]
    public void FullDocumentBindsEveryShape()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>("""
            format_version = 3
            name = "Full"
            max_speed = 12.5
            hardcore = true
            difficulty = "very_hard"
            tags = ["fast", "loud"]

            [engine]
            kind = "v8"
            boost = 2.5

            [[waves]]
            count = 1
            label = "first"

            [[waves]]
            count = 2
            """, "fixture.toml");

        Fixture fixture = Assert.IsType<Fixture>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, fixture.FormatVersion);
        Assert.Equal("Full", fixture.Name);
        Assert.Equal(12.5, fixture.Speed);
        Assert.True(fixture.Hardcore);
        Assert.Equal(Difficulty.VERY_HARD, fixture.Difficulty);
        Assert.Equal(["fast", "loud"], fixture.Tags!);
        Assert.Equal(new Engine("v8", 2.5f), fixture.Engine);
        Assert.Equal([new Wave(1, "first"), new Wave(2)], fixture.Waves);
    }

    [Fact]
    public void OmittedOptionalsFallBackToDeclaredDefaults()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>(
            "format_version = 3\nname = \"Bare\"\n", "fixture.toml");

        Fixture fixture = Assert.IsType<Fixture>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(50, fixture.Speed);
        Assert.False(fixture.Hardcore);
        Assert.Equal(Difficulty.EASY, fixture.Difficulty);
        Assert.Null(fixture.Tags);
        Assert.Null(fixture.Engine);
        Assert.Empty(fixture.Waves);
    }

    [Fact]
    public void UnknownKeysWarnWithTheirFullPath()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>("""
            format_version = 3
            name = "Typos"
            bogus = 1

            [engine]
            kind = "v8"
            turbo = true

            [[waves]]
            count = 1
            foo = "bar"
            """, "fixture.toml");

        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.All(result.Diagnostics,
            diagnostic => Assert.Equal(ContentDiagnosticSeverity.WARNING, diagnostic.Severity));
        Assert.Contains(result.Diagnostics, d => d.Message == "unknown key 'bogus'");
        Assert.Contains(result.Diagnostics, d => d.Message == "unknown key 'engine.turbo'");
        Assert.Contains(result.Diagnostics, d => d.Message == "unknown key 'waves[0].foo'");
    }

    [Fact]
    public void MissingRequiredKeysNameTheOwningTable()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>("""
            format_version = 3

            [engine]
            boost = 2.0

            [[waves]]
            label = "no count"
            """, "fixture.toml");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Message == "missing required key 'name'");
        Assert.Contains(result.Diagnostics, d => d.Message == "engine is missing required key 'kind'");
        Assert.Contains(result.Diagnostics, d => d.Message == "waves[0] is missing required key 'count'");
    }

    [Fact]
    public void TypeMismatchesReportPathAndExpectedType()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>("""
            format_version = 3
            name = 7
            hardcore = "yes"
            tags = ["ok", 3]

            [[waves]]
            count = 9999999999
            """, "fixture.toml");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Message == "'name' must be a string");
        Assert.Contains(result.Diagnostics, d => d.Message == "'hardcore' must be a boolean");
        Assert.Contains(result.Diagnostics, d => d.Message == "'tags[1]' must be a string");
        Assert.Contains(result.Diagnostics,
            d => d.Message == "'waves[0].count' must be a 32-bit integer");
    }

    [Fact]
    public void InvalidEnumIsAnError()
    {
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>("""
            format_version = 3
            name = "Wild"
            difficulty = "medium"
            """, "fixture.toml");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics,
            d => d.Message == "'difficulty' must be one of: easy, very_hard");
    }

    [Fact]
    public void WriteReadRoundTripsAndStaysDeterministic()
    {
        Fixture fixture = new()
        {
            FormatVersion = 3,
            Name = "Round \"Trip\"\n🐛",
            Waves = [new Wave(1, "first"), new Wave(2)],
            Speed = 12.5,
            Hardcore = true,
            Difficulty = Difficulty.VERY_HARD,
            Tags = ["fast", "loud"],
            Engine = new Engine("v8", 2.5f),
        };

        string text = TomlModel.Write(fixture);
        ContentReadResult<Fixture> result = TomlModel.Read<Fixture>(text, "fixture.toml");

        Fixture reparsed = Assert.IsType<Fixture>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(fixture.Name, reparsed.Name);
        Assert.Equal(fixture.Speed, reparsed.Speed);
        Assert.Equal(fixture.Difficulty, reparsed.Difficulty);
        Assert.Equal(fixture.Tags!, reparsed.Tags!);
        Assert.Equal(fixture.Engine, reparsed.Engine);
        Assert.Equal(fixture.Waves, reparsed.Waves);
        Assert.Equal(text, TomlModel.Write(reparsed));
    }

}
