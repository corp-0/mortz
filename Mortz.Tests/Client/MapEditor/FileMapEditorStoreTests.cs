using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class FileMapEditorStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"mortz-map-editor-store-{Guid.NewGuid():N}");

    [Fact]
    public void RasterOnlyGameplaySavePreservesAllPngBytesAndOmitsEditorDocument()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string[] names = ["background.png", "solid.png", "destructible.png"];
        Dictionary<string, byte[]> before = names.ToDictionary(name => name,
            name => File.ReadAllBytes(Path.Combine(definition.DirectoryPath, name)));
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore()).Workspace);

        workspace.AddSpawn(new MapSpawnPoint(3, 4));
        Assert.True(workspace.Save().Succeeded);

        Assert.Equal(MapEditorRasterSourceStatus.OBSOLETE, workspace.Snapshot.SourceStatus);
        Assert.All(names, name => Assert.Equal(before[name],
            File.ReadAllBytes(Path.Combine(definition.DirectoryPath, name))));
        Assert.False(File.Exists(Path.Combine(definition.DirectoryPath, "editor.json")));
    }

    [Fact]
    public void FutureEditorDocumentPreventsOpenAndIsNotOverwritten()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string editorPath = Path.Combine(definition.DirectoryPath, "editor.json");
        byte[] future = "{\"version\":99,\"nextBrushId\":1,\"layers\":[]}"u8.ToArray();
        File.WriteAllBytes(editorPath, future);

        MapEditorOpenResult result = MapEditorWorkspace.Open(definition, new FileMapEditorStore());

        Assert.Null(result.Workspace);
        Assert.Contains(Assert.IsType<MapEditorContentFailure>(result.Failure).Diagnostics,
            diagnostic => diagnostic.Message.Contains("newer", StringComparison.Ordinal));
        Assert.Equal(future, File.ReadAllBytes(editorPath));
    }

    [Theory]
    [InlineData("{\"version\":{},\"nextBrushId\":1,\"layers\":[]}")]
    [InlineData("{\"version\":1,\"nextBrushId\":9223372036854775808,\"layers\":[]}")]
    [InlineData("{\"version\":1,\"nextBrushId\":1,\"layers\":{}}")]
    public void MalformedCurrentEditorDocumentReturnsContentFailure(string json)
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        File.WriteAllText(Path.Combine(definition.DirectoryPath, "editor.json"), json);

        MapEditorOpenResult result = MapEditorWorkspace.Open(definition, new FileMapEditorStore());

        Assert.Null(result.Workspace);
        Assert.IsType<MapEditorContentFailure>(result.Failure);
    }

    [Fact]
    public void StoreCannotOverwriteExistingFutureEditorDocument()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string editorPath = Path.Combine(definition.DirectoryPath, "editor.json");
        byte[] future = "{\"version\":99,\"nextBrushId\":1,\"layers\":[]}"u8.ToArray();
        File.WriteAllBytes(editorPath, future);
        MapEditorLayers layers = new(
            new MapEditorLayerAsset(File.ReadAllBytes(Path.Combine(definition.DirectoryPath,
                "background.png")), 8, 8),
            new MapEditorLayerAsset(File.ReadAllBytes(Path.Combine(definition.DirectoryPath,
                "solid.png")), 8, 8),
            new MapEditorLayerAsset(File.ReadAllBytes(Path.Combine(definition.DirectoryPath,
                "destructible.png")), 8, 8));
        MapEditorBrushDocument replacement = new(MapEditorBrushDocument.CURRENT_VERSION, 1,
            new MapEditorLayerSources(
                new MapEditorLayerSource([], layers.Background, false),
                new MapEditorLayerSource([], layers.Solid, false),
                new MapEditorLayerSource([], layers.Destructible, false)));

        MapEditorStoreResult<ContentDefinition<MapManifest>> result = new FileMapEditorStore().Save(
            definition, definition.Manifest, layers, 8, 8, replacement);

        Assert.IsType<MapEditorContentFailure>(result.Failure);
        Assert.Equal(future, File.ReadAllBytes(editorPath));
    }

    [Fact]
    public void SourceDocumentIsWrittenWithRuntimeFilesAndReopensWithStableIdentity()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        FileMapEditorStore store = new();
        MapEditorWorkspace raster = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);
        MapEditorBrush brush = new(new MapEditorBrushId(42), "ground",
            MapEditorLayer.BACKGROUND, new MapEditorRectBrushShape(1, 1, 4, 4, 0),
            new MapEditorTextureMaterial(MapEditorTextureReference.Project("Assets/ground.png")),
            new MapEditorTextureProjection(MapEditorProjectionMode.STRETCH,
                new MapEditorPoint(1, 1), 1, 1, 0), true);
        MapEditorBrushDocument document = new(MapEditorBrushDocument.CURRENT_VERSION, 43,
            new MapEditorLayerSources(
                new MapEditorLayerSource([brush], raster.Snapshot.Layers.Background, false),
                new MapEditorLayerSource([], raster.Snapshot.Layers.Solid, false),
                new MapEditorLayerSource([], raster.Snapshot.Layers.Destructible, false)));

        Assert.True(store.Save(definition, raster.BuildManifest(), raster.Snapshot.Layers,
            8, 8, document).Succeeded);
        MapEditorWorkspace reopened = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);

        Assert.Equal(MapEditorRasterSourceStatus.BRUSH_SOURCE, reopened.Snapshot.SourceStatus);
        Assert.Equal(new MapEditorBrushId(42),
            Assert.Single(reopened.Snapshot.BrushDocument!.Layers.Background.Brushes).Id);
        Assert.Equal(43, reopened.Snapshot.BrushDocument.NextBrushId);
        Assert.True(File.Exists(Path.Combine(definition.DirectoryPath, "editor.json")));
    }

    [Fact]
    public void SaveThenReopenPreservesCompleteManifestAndAllLayerBytes()
    {
        MapManifest manifest = new()
        {
            Name = "Complete Map",
            SuggestedPlayers = 2,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "gravity",
                    Tags = ["playable", "low-gravity"],
                    Shape = new RectMapZoneShape(1, 1, 3, 4, 15),
                    Effects = [new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 0.5f)],
                },
                new MapZoneDef
                {
                    Name = "circle",
                    Tags = ["goal"],
                    Shape = new CircleMapZoneShape(5, 5, 2),
                },
            ],
            SpawnPoints = [new MapSpawnPoint(2, 3, Team.BLUE)],
        };
        ContentDefinition<MapManifest> definition = CreatePackage(manifest);
        byte[] background = File.ReadAllBytes(Path.Combine(definition.DirectoryPath, "background.png"));
        byte[] solid = File.ReadAllBytes(Path.Combine(definition.DirectoryPath, "solid.png"));
        byte[] destructible = File.ReadAllBytes(Path.Combine(definition.DirectoryPath, "destructible.png"));
        FileMapEditorStore store = new();
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);
        workspace.AddSpawn(new MapSpawnPoint(6, 6, Team.RED));
        MapManifest expectedManifest = workspace.BuildManifest();

        Assert.True(workspace.Save().Succeeded);
        MapEditorWorkspace reopened = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);

        Assert.Equal(TomlModel.Write(expectedManifest), TomlModel.Write(reopened.BuildManifest()));
        Assert.Equal(background, reopened.Snapshot.Layers.Background.Png.ToArray());
        Assert.Equal(solid, reopened.Snapshot.Layers.Solid.Png.ToArray());
        Assert.Equal(destructible, reopened.Snapshot.Layers.Destructible.Png.ToArray());
        Assert.Equal(background, File.ReadAllBytes(Path.Combine(
            definition.DirectoryPath, "background.png")));
        Assert.Equal(solid, File.ReadAllBytes(Path.Combine(
            definition.DirectoryPath, "solid.png")));
        Assert.Equal(destructible, File.ReadAllBytes(Path.Combine(
            definition.DirectoryPath, "destructible.png")));
        Assert.False(reopened.Snapshot.Dirty);
    }

    private ContentDefinition<MapManifest> CreatePackage(MapManifest? manifest = null)
    {
        string packDirectory = Path.Combine(_root, "pack");
        string mapsDirectory = Path.Combine(packDirectory, "maps");
        ContentPackDefinition pack = new(
            new ContentPackManifest("org.example.test", "Test", "1"), packDirectory);
        manifest ??= new MapManifest { Name = "Test", SuggestedPlayers = 1 };
        Image blank = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        blank.Fill(Colors.Transparent);
        MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
            "test", manifest, blank.SavePngToBuffer(), blank.SavePngToBuffer(),
            blank.SavePngToBuffer()));
        string mapDirectory = Path.Combine(mapsDirectory, "test");
        return new ContentDefinition<MapManifest>("test", manifest, mapDirectory,
            Path.Combine(mapDirectory, "map.toml"), pack);
    }

    private byte[] WritePng(string name, Color color)
    {
        string path = Path.Combine(_root, name);
        Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        image.Fill(color);
        Assert.Equal(Error.Ok, image.SavePng(path));
        return File.ReadAllBytes(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
