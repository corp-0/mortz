using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Shared;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorLayerTests
{
    [Fact]
    public void ReplacingALayerMarksTheMapDirtyAndSavesTheImage()
    {
        using MapFixture fixture = new();
        string replacementPath = fixture.WriteImage("replacement.png", 8, 8, Colors.Red);
        Mortz.Client.MapEditor.MapEditor editor = new();
        MapEditorLayer? changedLayer = null;
        editor.LayerChanged += (layer, _) => changedLayer = layer;
        editor.Open(fixture.Definition);

        editor.ReplaceLayer(MapEditorLayer.BACKGROUND, replacementPath);
        editor.Save();

        MapPackage package = Assert.IsType<MapPackage>(
            MapPackageLoader.Load(fixture.Definition).Package);
        Assert.Equal(MapEditorLayer.BACKGROUND, changedLayer);
        Assert.False(editor.Dirty);
        Assert.Equal(Colors.Red, package.Background.GetPixel(0, 0));
        editor.Free();
    }

    [Fact]
    public void ReplacementMustMatchTheMapDimensions()
    {
        using MapFixture fixture = new();
        string replacementPath = fixture.WriteImage("wrong-size.png", 4, 8, Colors.Red);
        Mortz.Client.MapEditor.MapEditor editor = new();
        string? status = null;
        editor.StatusChanged += (text, error) => status = error ? text : status;
        editor.Open(fixture.Definition);

        editor.ReplaceLayer(MapEditorLayer.SOLID, replacementPath);

        Assert.False(editor.Dirty);
        Assert.Contains("must be 8 x 8 px", status);
        editor.Free();
    }

    private sealed class MapFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            $"mortz-map-editor-{Guid.NewGuid():N}");

        public MapFixture()
        {
            string packDirectory = Path.Combine(_root, "pack");
            string mapsDirectory = Path.Combine(packDirectory, "maps");
            ContentPackDefinition pack = new(
                new ContentPackManifest("com.example.test", "Test", "1.0.0"),
                packDirectory);
            MapManifest manifest = new() { Name = "Test", SuggestedPlayers = 1 };
            Image blank = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            blank.Fill(Colors.Transparent);
            MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
                "test", manifest, blank.SavePngToBuffer(), blank.SavePngToBuffer(),
                blank.SavePngToBuffer()));
            string mapDirectory = Path.Combine(mapsDirectory, "test");
            Definition = new ContentDefinition<MapManifest>("test", manifest, mapDirectory,
                Path.Combine(mapDirectory, "map.toml"), pack);
        }

        public ContentDefinition<MapManifest> Definition { get; }

        public string WriteImage(string fileName, int width, int height, Color color)
        {
            string path = Path.Combine(_root, fileName);
            Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            image.Fill(color);
            Assert.Equal(Error.Ok, image.SavePng(path));
            return path;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
