using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Sim.Modifiers;
using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Content;

public sealed class MapManifestValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"mortz-map-validation-{Guid.NewGuid():N}");

    [Fact]
    public void ReportsExpectedManifestDiagnostics()
    {
        MapZoneDef[] zones = Enumerable.Range(0, MapZones.MAX_EFFECT_ZONES + 1)
            .Select(index => new MapZoneDef
            {
                Name = index == 0 ? "Bad Name" : "zone_" + index,
                Shape = index == 0
                    ? new RectMapZoneShape(0, 0, 0, 10, float.NaN)
                    : new CircleMapZoneShape(10, 10, 1),
                Effects =
                [
                    new MapZoneEffect(Stat.GRAVITY, StatOp.MUL,
                        index == 0 ? float.PositiveInfinity : 1),
                ],
            })
            .ToArray();
        zones[2] = zones[2] with { Name = zones[1].Name };
        MapManifest manifest = new()
        {
            Name = "Invalid",
            SuggestedPlayers = NetConfig.MAX_PLAYERS + 1,
            SpawnPoints = [new MapSpawnPoint(0, 0, (Team)99)],
            Zones = zones,
        };

        IReadOnlyList<ContentDiagnostic> diagnostics =
            MapManifestValidator.Validate(manifest, "map.toml");

        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(ContentDiagnosticSeverity.ERROR, diagnostic.Severity));
        Assert.Contains(diagnostics, d => d.Message.Contains("suggested_players"));
        Assert.Contains(diagnostics, d => d.Message.Contains("spawn_points[0].team"));
        Assert.Contains(diagnostics, d => d.Message.Contains("not a valid logical name"));
        Assert.Contains(diagnostics, d => d.Message.Contains("duplicates zone name"));
        Assert.Contains(diagnostics, d => d.Message.Contains("shape.width must be positive"));
        Assert.Contains(diagnostics, d => d.Message.Contains("shape.rotation must be finite"));
        Assert.Contains(diagnostics, d => d.Message.Contains("effects[0].value must be finite"));
        Assert.Contains(diagnostics, d => d.Message.Contains("maximum is 64"));
    }

    [Fact]
    public void BoundsIncludeRotatedShapesAndSpawnPoints()
    {
        MapManifest manifest = new()
        {
            Name = "Bounds",
            SuggestedPlayers = 1,
            SpawnPoints = [new MapSpawnPoint(100, 10)],
            Zones =
            [
                new MapZoneDef
                {
                    Name = "rotated_rect",
                    Shape = new RectMapZoneShape(0, 0, 20, 20, 45),
                },
                new MapZoneDef
                {
                    Name = "rotated_ellipse",
                    Shape = new EllipseMapZoneShape(90, 50, 20, 5, 45),
                },
            ],
        };

        IReadOnlyList<ContentDiagnostic> diagnostics = MapManifestValidator.Validate(
            manifest, "map.toml", new MapDimensions(100, 100));

        Assert.Contains(diagnostics, d => d.Message.Contains("spawn_points[0]"));
        Assert.Contains(diagnostics, d => d.Message.Contains("zones[0].shape"));
        Assert.Contains(diagnostics, d => d.Message.Contains("zones[1].shape"));
    }

    [Fact]
    public void ReportingModeKeepsTheSameMessagesWithoutHardErrors()
    {
        MapManifest manifest = new() { Name = "Invalid", SuggestedPlayers = 0 };

        ContentDiagnostic enforced = Assert.Single(MapManifestValidator.Validate(
            manifest, "map.toml"));
        ContentDiagnostic reported = Assert.Single(MapManifestValidator.Validate(
            manifest, "map.toml", mode: ContentValidationMode.REPORT));

        Assert.Equal(enforced.Message, reported.Message);
        Assert.Equal(ContentDiagnosticSeverity.ERROR, enforced.Severity);
        Assert.Equal(ContentDiagnosticSeverity.WARNING, reported.Severity);
    }

    [Fact]
    public void SourceAndWriterUseTheSameSemanticDiagnostic()
    {
        MapManifest manifest = new() { Name = "Invalid", SuggestedPlayers = 0 };
        ContentDefinition<MapManifest> definition = WriteRawMap("invalid", manifest,
            "not-a-png"u8.ToArray());

        ContentReadResult<MapSourceSnapshot> source = MapSourceSnapshot.Read(definition);
        ContentValidationException writer = Assert.Throws<ContentValidationException>(() =>
            MapPackageWriter.Write(Path.Combine(_root, "written"),
                new MapPackageWriteRequest("invalid", manifest,
                    "background"u8.ToArray(), "solid"u8.ToArray(),
                    "destructible"u8.ToArray())));

        Assert.Null(source.Value);
        Assert.Equal(Assert.Single(source.Diagnostics).Message,
            Assert.Single(writer.Diagnostics).Message);
    }

    [Fact]
    public void WriterUsesKnownImageDimensions()
    {
        MapManifest manifest = new()
        {
            Name = "Invalid",
            SuggestedPlayers = 1,
            SpawnPoints = [new MapSpawnPoint(20, 5)],
        };

        ContentValidationException exception = Assert.Throws<ContentValidationException>(() =>
            MapPackageWriter.Write(Path.Combine(_root, "written"),
                new MapPackageWriteRequest("invalid", manifest,
                    "background"u8.ToArray(), "solid"u8.ToArray(),
                    "destructible"u8.ToArray(), 10, 10)));

        Assert.Contains(exception.Diagnostics,
            diagnostic => diagnostic.Message.Contains("spawn_points[0]"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ContentDefinition<MapManifest> WriteRawMap(string id, MapManifest manifest,
        byte[] layer)
    {
        string packDirectory = Path.Combine(_root, "pack");
        string mapDirectory = Path.Combine(packDirectory, "maps", id);
        Directory.CreateDirectory(mapDirectory);
        string manifestPath = Path.Combine(mapDirectory, "map.toml");
        File.WriteAllText(manifestPath, TomlModel.Write(manifest));
        foreach (string name in new[] { "background.png", "solid.png", "destructible.png" })
        {
            File.WriteAllBytes(Path.Combine(mapDirectory, name), layer);
        }
        ContentPackDefinition pack = new(
            new ContentPackManifest("org.mortz.test", "Test", "1"),
            packDirectory);
        return new ContentDefinition<MapManifest>(id, manifest, mapDirectory, manifestPath, pack);
    }
}

[Collection(nameof(MortzGodotCollection))]
public sealed class MapManifestLoaderValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"mortz-map-loader-validation-{Guid.NewGuid():N}");

    [Fact]
    public void LoaderValidatesBoundsBeforeRuntimeCompilation()
    {
        Image image = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8);
        MapManifest manifest = new()
        {
            Name = "Invalid",
            SuggestedPlayers = 1,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "outside",
                    Shape = new CircleMapZoneShape(9, 5, 2),
                },
            ],
        };
        string mapsDirectory = Path.Combine(_root, "maps");
        MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
            "invalid", manifest, image.SavePngToBuffer(), image.SavePngToBuffer(),
            image.SavePngToBuffer()));
        string mapDirectory = Path.Combine(mapsDirectory, "invalid");
        string manifestPath = Path.Combine(mapDirectory, "map.toml");
        ContentDefinition<MapManifest> definition = new("invalid", manifest,
            mapDirectory, manifestPath,
            new ContentPackDefinition(
                new ContentPackManifest("org.mortz.test", "Test", "1"), _root));

        MapPackageLoadResult result = MapPackageLoader.Load(definition);

        Assert.Null(result.Package);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("zones[0].shape extends outside"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
