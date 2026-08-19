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
    public void SavePersistsManifestAndExactReplacementPngBytes()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string replacementPath = Path.Combine(_root, "replacement.png");
        Image replacement = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        replacement.Fill(Colors.Red);
        Assert.Equal(Error.Ok, replacement.SavePng(replacementPath));
        byte[] replacementBytes = File.ReadAllBytes(replacementPath);

        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore()).Workspace);
        workspace.AddSpawn(new MapSpawnPoint(3, 4));
        Assert.True(workspace.ReplaceLayer(MapEditorLayer.BACKGROUND, replacementPath).Succeeded);
        Assert.True(workspace.Save().Succeeded);

        Assert.Equal(replacementBytes,
            File.ReadAllBytes(Path.Combine(definition.DirectoryPath, "background.png")));
        ContentReadResult<MapManifest> manifest = TomlModel.ReadFile<MapManifest>(
            definition.ManifestPath);
        Assert.Equal(new MapSpawnPoint(3, 4), Assert.Single(manifest.Value!.SpawnPoints));
        Assert.False(workspace.Snapshot.Dirty);
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
        byte[] background = WritePng("new-background.png", new Color(0.8f, 0.1f, 0.2f));
        byte[] solid = WritePng("new-solid.png", new Color(0.1f, 0.8f, 0.2f));
        byte[] destructible = WritePng("new-destructible.png", new Color(0.1f, 0.2f, 0.8f));
        FileMapEditorStore store = new();
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);
        workspace.AddSpawn(new MapSpawnPoint(6, 6, Team.RED));
        Assert.True(workspace.ReplaceLayer(MapEditorLayer.BACKGROUND,
            Path.Combine(_root, "new-background.png")).Succeeded);
        Assert.True(workspace.ReplaceLayer(MapEditorLayer.SOLID,
            Path.Combine(_root, "new-solid.png")).Succeeded);
        Assert.True(workspace.ReplaceLayer(MapEditorLayer.DESTRUCTIBLE,
            Path.Combine(_root, "new-destructible.png")).Succeeded);
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

    [Fact]
    public void InvalidPngReturnsTypedFailureWithoutChangingWorkspace()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string invalidPath = Path.Combine(_root, "invalid.png");
        File.WriteAllBytes(invalidPath, "not png"u8.ToArray());
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore()).Workspace);
        MapEditorSnapshot before = workspace.Snapshot;

        MapEditorOperationResult result = workspace.ReplaceLayer(
            MapEditorLayer.SOLID, invalidPath);

        Assert.IsType<MapEditorInvalidPngFailure>(result.Failure);
        Assert.Same(before, workspace.Snapshot);
        Assert.Equal(before.Revision, workspace.Snapshot.Revision);
    }

    [Fact]
    public void WrongSizePngReturnsTypedFailureWithoutChangingWorkspace()
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        string wrongSizePath = Path.Combine(_root, "wrong-size.png");
        Image replacement = Image.CreateEmpty(4, 8, false, Image.Format.Rgba8);
        Assert.Equal(Error.Ok, replacement.SavePng(wrongSizePath));
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore()).Workspace);
        MapEditorSnapshot before = workspace.Snapshot;

        MapEditorOperationResult result = workspace.ReplaceLayer(
            MapEditorLayer.DESTRUCTIBLE, wrongSizePath);

        MapEditorLayerSizeFailure failure = Assert.IsType<MapEditorLayerSizeFailure>(
            result.Failure);
        Assert.Equal((8, 8, 4, 8), (failure.ExpectedWidth, failure.ExpectedHeight,
            failure.ActualWidth, failure.ActualHeight));
        Assert.Same(before, workspace.Snapshot);
        Assert.Equal(before.Revision, workspace.Snapshot.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingReplacementPathReturnsTypedFailureWithoutChangingWorkspace(string? path)
    {
        ContentDefinition<MapManifest> definition = CreatePackage();
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore()).Workspace);
        MapEditorSnapshot before = workspace.Snapshot;

        MapEditorOperationResult result = workspace.ReplaceLayer(
            MapEditorLayer.SOLID, path);

        Assert.IsType<MapEditorIoFailure>(result.Failure);
        Assert.Same(before, workspace.Snapshot);
        Assert.Equal(before.Revision, workspace.Snapshot.Revision);
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
