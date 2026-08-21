using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;
using Engine = twodog.Engine;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorBundledFlatTests : IDisposable
{
    private readonly string _temporaryPackageRoot = Path.Combine(Path.GetTempPath(),
        $"mortz-flat-source-{Guid.NewGuid():N}");

    [Fact]
    public void FlatSourceLoadsAndRuntimeLayersMatchTheCompositor()
    {
        ContentDefinition<MapManifest> definition = FlatDefinition(FlatDirectory());
        MapEditorStoredMap stored = Assert.IsType<MapEditorStoredMap>(
            new FileMapEditorStore().Load(definition).Value);
        MapEditorBrushDocument document =
            Assert.IsType<MapEditorBrushDocument>(stored.BrushDocument);
        MapEditorLayerCompositor compositor = new(new MapEditorTextureResolver());
        Dictionary<MapEditorLayer, MapEditorLayerAsset> composed = [];
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorLayerCompositionResult result = compositor.Compose(
                document.Layers.Get(layer), stored.Layers.Background.Width,
                stored.Layers.Background.Height);
            Assert.True(result.Succeeded, result.Error);
            composed[layer] = result.Baked!;
        }

        Assert.Equal(stored.Layers.Background.Png.ToArray(),
            composed[MapEditorLayer.BACKGROUND].Png.ToArray());
        Assert.Equal(stored.Layers.Solid.Png.ToArray(),
            composed[MapEditorLayer.SOLID].Png.ToArray());
        Assert.Equal(stored.Layers.Destructible.Png.ToArray(),
            composed[MapEditorLayer.DESTRUCTIBLE].Png.ToArray());
        Assert.Equal(MapEditorRasterSourceStatus.BRUSH_SOURCE,
            Assert.IsType<MapEditorWorkspace>(MapEditorWorkspace.Open(definition,
                new FileMapEditorStore()).Workspace).Snapshot.SourceStatus);
    }

    [Fact]
    public void FlatSourcePackageLoadsSavesAndReloadsWithoutChangingRuntimeLayers()
    {
        string temporaryFlat = Path.Combine(_temporaryPackageRoot, "maps", "flat");
        Directory.CreateDirectory(temporaryFlat);
        foreach (string source in Directory.EnumerateFiles(FlatDirectory()))
        {
            File.Copy(source, Path.Combine(temporaryFlat, Path.GetFileName(source)));
        }
        ContentDefinition<MapManifest> definition = FlatDefinition(temporaryFlat);
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, new FileMapEditorStore(),
                new MapEditorTextureResolver()).Workspace);
        Dictionary<string, byte[]> runtimeBefore = RuntimeFiles().ToDictionary(name => name,
            name => File.ReadAllBytes(Path.Combine(temporaryFlat, name)));
        MapEditorBrush first = workspace.Snapshot.BrushDocument!.Layers.Background.Brushes[0];

        Assert.True(workspace.ReplaceBrush(first.Id,
            new MapEditorBrushDraft(first.Name + " renamed", first.Layer, first.Shape,
                first.Material, first.Projection, first.Visible)).Succeeded);
        MapEditorOperationResult saved = workspace.Save();
        Assert.True(saved.Succeeded, saved.Failure?.ToString());
        Assert.All(RuntimeFiles(), name => Assert.Equal(runtimeBefore[name],
            File.ReadAllBytes(Path.Combine(temporaryFlat, name))));

        MapEditorOperationResult reloaded = workspace.Reload();
        Assert.True(reloaded.Succeeded);
        Assert.Equal(first.Id, workspace.Snapshot.BrushDocument!.Layers.Background.Brushes[0].Id);
        Assert.Equal(first.Name + " renamed",
            workspace.Snapshot.BrushDocument.Layers.Background.Brushes[0].Name);
    }

    private static ContentDefinition<MapManifest> FlatDefinition(string directory)
    {
        string manifestPath = Path.Combine(directory, "map.toml");
        MapManifest manifest = Assert.IsType<MapManifest>(
            TomlModel.Read<MapManifest>(File.ReadAllText(manifestPath)).Value);
        string packDirectory = Path.GetDirectoryName(Path.GetDirectoryName(directory))!;
        ContentPackDefinition pack = new(
            new ContentPackManifest("org.mortz.base", "Base", "1"), packDirectory);
        return new ContentDefinition<MapManifest>("flat", manifest, directory, manifestPath, pack);
    }

    private static string FlatDirectory() => Path.Combine(Engine.ResolveProjectDir(),
        "content", "Base", "maps", "flat");

    private static string[] RuntimeFiles() =>
        ["background.png", "solid.png", "destructible.png"];

    public void Dispose()
    {
        if (Directory.Exists(_temporaryPackageRoot))
            Directory.Delete(_temporaryPackageRoot, recursive: true);
    }
}
