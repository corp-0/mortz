using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Content;

public class ManifestTests
{
    [Fact]
    public void MapManifestRequiresVersionNameAndSuggestedPlayers()
    {
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap("name = \"Arena\"\n");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("format_version", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("suggested_players", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedMapVersionSaysWhichVersionItWanted()
    {
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap(
            "format_version = 2\nname = \"Arena\"\nsuggested_players = 4\n",
            "custom/map.toml");

        Assert.Null(result.Value);
        ContentDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticSeverity.Error, error.Severity);
        Assert.Equal("custom/map.toml", error.Source);
        Assert.Contains("unsupported format_version 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMapKeyWarnsButLoads()
    {
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap(
            "format_version = 1\nname = \"Arena\"\nsuggested_players = 4\nsugested_players = 8\n");

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Equal(4, manifest.SuggestedPlayers);
        ContentDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(ContentDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("sugested_players", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizedMapRoundTripsEscapedNames()
    {
        MapManifest expected = new(1, "Gilles' \"Arena\"\nTwo", 8);

        string text = ContentManifestReader.WriteMap(expected);
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap(text);

        Assert.Equal(expected, result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            "format_version = 1\nname = \"Gilles' \\\"Arena\\\"\\nTwo\"\nsuggested_players = 8\n",
            text);
    }

    [Fact]
    public void NormalizedMapEscapesAllTomlControlCharactersAndPreservesUnicode()
    {
        MapManifest expected = new(1, "A\0B\vC\u001FD\u007F 🐛", 2);

        string text = ContentManifestReader.WriteMap(expected);
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap(text);

        Assert.Equal(expected.Name, Assert.IsType<MapManifest>(result.Value).Name);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("\\u0000", text, StringComparison.Ordinal);
        Assert.Contains("\\u000B", text, StringComparison.Ordinal);
        Assert.Contains("\\u001F", text, StringComparison.Ordinal);
        Assert.Contains("\\u007F", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizedMapRejectsUnpairedUtf16Surrogates()
    {
        MapManifest invalid = new(1, "bad \uD800 name", 2);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ContentManifestReader.WriteMap(invalid));

        Assert.Contains("unpaired", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackValidatesLogicalId()
    {
        ContentReadResult<ContentPackManifest> result = ContentManifestReader.ReadPack(
            "id = \"../Base\"\nname = \"Bad\"\nversion = \"1\"\n");

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("lowercase", StringComparison.Ordinal));
    }

    [Fact]
    public void SpawnPointsParseInAuthoredOrderAndRoundTrip()
    {
        const string TEXT = """
            format_version = 1
            name = "Arena"
            suggested_players = 2

            [[spawn_points]]
            x = 100
            y = 250

            [[spawn_points]]
            x = 300
            y = 250
            """;

        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap(TEXT);

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Equal([new MapSpawnPoint(100, 250), new MapSpawnPoint(300, 250)],
            manifest.SpawnPoints);
        string normalized = ContentManifestReader.WriteMap(manifest);
        Assert.Equal(manifest.SpawnPoints,
            Assert.IsType<MapManifest>(ContentManifestReader.ReadMap(normalized).Value).SpawnPoints);
    }

    [Fact]
    public void MalformedSpawnIdentifiesEntryAndField()
    {
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap("""
            format_version = 1
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

    [Fact]
    public void DuplicateSpawnPointsAreRejected()
    {
        ContentReadResult<MapManifest> result = ContentManifestReader.ReadMap("""
            format_version = 1
            name = "Arena"
            suggested_players = 2

            [[spawn_points]]
            x = 100
            y = 250

            [[spawn_points]]
            x = 100
            y = 250
            """);

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "spawn_points[1] duplicates spawn_points[0]", StringComparison.Ordinal));
    }
}
