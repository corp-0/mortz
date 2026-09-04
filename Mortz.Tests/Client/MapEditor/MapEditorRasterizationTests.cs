using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public class MapEditorRasterizationTests(MortzGodotFixture fixture)
{
    [Fact]
    public void RasterizePreservesPixelsBoundsAndHistory()
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(-7, -3, 8, 6, 30));
        Add(workspace, new MapEditorEllipseBrushShape(1, 1, 5, 3, 45),
            color: new MapEditorColor(30, 80, 240, 128));
        Add(workspace, new MapEditorPolygonBrushShape([new(4, 0), new(8, 7), new(3, 6)]));
        MapEditorSnapshot before = workspace.Snapshot;
        byte[] pixels = Bake(before).Png.ToArray();

        MapEditorOperationResult result = workspace.RasterizeBrushes(Ids(before));

        Assert.True(result.Succeeded, result.Failure?.ToString());
        MapEditorBrush raster = Assert.Single(Brushes(workspace.Snapshot));
        Assert.IsType<MapEditorRasterMaterial>(raster.Material);
        Assert.Equal(before.Bounds, workspace.Snapshot.Bounds);
        Assert.Equal(pixels, Bake(workspace.Snapshot).Png.ToArray());
        Assert.True(workspace.Undo().Succeeded);
        Assert.Equal(Brushes(before), Brushes(workspace.Snapshot));
        Assert.True(workspace.Redo().Succeeded);
        Assert.Same(raster, Assert.Single(Brushes(workspace.Snapshot)));
    }

    [Fact]
    public void RasterDataSurvivesSerializationAndStampReuseWithoutSourceTextures()
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(-3, 2, 2, 2, 0));
        Add(workspace, new MapEditorRectBrushShape(2, 2, 2, 2, 0));
        Assert.True(workspace.RasterizeBrushes(Ids(workspace.Snapshot)).Succeeded);
        MapEditorBrush raster = Assert.Single(Brushes(workspace.Snapshot));
        Assert.True(workspace.SaveStamp(raster.Id).Succeeded);
        MapEditorBrushDocument document = workspace.Snapshot.BrushDocument!;

        MapEditorBrushDocument loaded = MapEditorDocumentJson.Deserialize(
            MapEditorDocumentJson.Serialize(document), workspace.Snapshot.Layers);

        MapEditorRasterMaterial restored = Assert.IsType<MapEditorRasterMaterial>(
            Assert.Single(loaded.Layers.Background.Brushes).Material);
        Assert.Equal(Assert.IsType<MapEditorRasterMaterial>(raster.Material).Image.Png.ToArray(),
            restored.Image.Png.ToArray());
        Assert.IsType<MapEditorRasterMaterial>(Assert.Single(loaded.Stamps).Brush.Material);
        Assert.Equal(Bake(workspace.Snapshot).Png.ToArray(),
            Compositor().Compose(loaded.Layers.Background, workspace.Snapshot.Bounds).Baked!.Png.ToArray());
    }

    [Fact]
    public void TransparentRasterPixelsDoNotInterceptPicking()
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(0, 0, 1, 1, 0));
        Add(workspace, new MapEditorRectBrushShape(3, 0, 1, 1, 0));
        Assert.True(workspace.RasterizeBrushes(Ids(workspace.Snapshot)).Succeeded);
        MapEditorCanvasPicker picker = new();
        Assert.Null(picker.PickBrush(workspace.Snapshot, MapEditorLayer.BACKGROUND,
            null, null, new Vector2(2, 0), 1, true, false, Vector2.Zero));
        Assert.NotNull(picker.PickBrush(workspace.Snapshot, MapEditorLayer.BACKGROUND,
            null, null, Vector2.Zero, 1, true, false, Vector2.Zero));
    }

    [Theory]
    [InlineData("interleaved")]
    [InlineData("layers")]
    [InlineData("hidden")]
    [InlineData("missing")]
    [InlineData("oversized")]
    [InlineData("stale")]
    public void InvalidSelectionsLeaveTheDocumentAndHistoryUntouched(string scenario)
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(0, 0, 1, 1, 0));
        Add(workspace, new MapEditorRectBrushShape(scenario == "oversized" ? 9000 : 1, 0, 1, 1, 0),
            layer: scenario == "layers" ? MapEditorLayer.SOLID : MapEditorLayer.BACKGROUND,
            visible: scenario != "hidden", missing: scenario == "missing");
        HashSet<MapEditorBrushId> ids = [new(1), new(2)];
        if (scenario == "interleaved")
        {
            Add(workspace, new MapEditorRectBrushShape(2, 0, 1, 1, 0));
            ids = [new(1), new(3)];
        }
        if (scenario == "stale")
            ids.Add(new(99));
        MapEditorSnapshot before = workspace.Snapshot;

        Assert.False(workspace.RasterizeBrushes(ids).Succeeded);
        Assert.Same(before, workspace.Snapshot);
    }

    [Fact]
    public void RasterizeSubsetKeepsUnselectedObjectsAndStackPosition()
    {
        MapEditorWorkspace workspace = Workspace();
        for (int i = 0; i < 4; i++)
        {
            Add(workspace, new MapEditorRectBrushShape(i, 0, 1, 1, 0));
        }
        MapEditorBrush first = Brushes(workspace.Snapshot)[0];
        MapEditorBrush last = Brushes(workspace.Snapshot)[3];
        Assert.True(workspace.RasterizeBrushes(new HashSet<MapEditorBrushId> { new(2), new(3) }).Succeeded);
        Assert.Collection(Brushes(workspace.Snapshot),
            brush => Assert.Same(first, brush),
            brush => Assert.IsType<MapEditorRasterMaterial>(brush.Material),
            brush => Assert.Same(last, brush));
    }

    [Fact]
    public void MultiSelectionTogglesAndClearsWhenChangingLayerOrDomain()
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(0, 0, 1, 1, 0));
        Add(workspace, new MapEditorRectBrushShape(1, 0, 1, 1, 0));
        Add(workspace, new MapEditorRectBrushShape(2, 0, 1, 1, 0), visible: false);
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.SelectBrush(new(1));
        interaction.ToggleBrushSelection(new(2));
        Assert.Equal(2, interaction.SelectedBrushIds.Count);
        Assert.Null(interaction.SelectedBrushId);
        interaction.ToggleBrushSelection(new(1));
        Assert.Equal(new MapEditorBrushId(2), interaction.SelectedBrushId);
        interaction.SelectAllBrushes();
        Assert.Equal(2, interaction.SelectedBrushIds.Count);
        interaction.SelectLayer(MapEditorLayer.SOLID);
        Assert.Empty(interaction.SelectedBrushIds);
        interaction.SelectLayer(MapEditorLayer.BACKGROUND);
        interaction.SelectAllBrushes();
        interaction.SetEditDomain(MapEditorEditDomain.ZONES);
        Assert.Empty(interaction.SelectedBrushIds);
    }

    [Fact]
    public void VersionTwoDocumentsStillLoadAndInvalidRasterDataIsRejected()
    {
        MapEditorWorkspace workspace = Workspace();
        Add(workspace, new MapEditorRectBrushShape(0, 0, 1, 1, 0));
        JsonNode json = JsonNode.Parse(MapEditorDocumentJson.Serialize(workspace.Snapshot.BrushDocument!))!;
        json["version"] = 2;
        MapEditorBrushDocument loaded = MapEditorDocumentJson.Deserialize(
            Encoding.UTF8.GetBytes(json.ToJsonString()), workspace.Snapshot.Layers);
        Assert.Equal(MapEditorBrushDocument.CURRENT_VERSION, loaded.Version);
        Assert.Single(loaded.Layers.Background.Brushes);
        Assert.Throws<ArgumentException>(() => new MapEditorRasterMaterial(new byte[] { 1, 2, 3 }));
        json["layers"]![0]!["brushes"]![0]!["material"] = new JsonObject
        {
            ["kind"] = "RASTER",
            ["png"] = "invalid base64",
        };
        Assert.Throws<JsonException>(() => MapEditorDocumentJson.Deserialize(
            Encoding.UTF8.GetBytes(json.ToJsonString()), workspace.Snapshot.Layers));
    }

    [Fact]
    public void ToolbarControlsResolveAndEmitLayerAndRasterizeRequests()
    {
        MapEditorToolbar toolbar = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/MapEditor/MapEditorToolbar.tscn").Instantiate<MapEditorToolbar>();
        try
        {
            toolbar._Ready();
            MapEditorLayer? selected = null;
            bool rasterize = false;
            toolbar.LayerSelected += layer => selected = layer;
            toolbar.RasterizeRequested += () => rasterize = true;
            OptionButton layer = Export<OptionButton>(toolbar, "_workingLayer");
            Button raster = Export<Button>(toolbar, "_rasterize");
            toolbar.ApplyLayer(MapEditorLayer.SOLID);
            Assert.Equal(1, layer.Selected);
            Assert.Null(selected);
            layer.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            Assert.Equal(MapEditorLayer.DESTRUCTIBLE, selected);
            toolbar.ApplySelection(3, true);
            Assert.False(raster.Disabled);
            raster.EmitSignal(BaseButton.SignalName.Pressed);
            Assert.True(rasterize);
            toolbar.ApplySelection(1, true);
            Assert.True(raster.Disabled);
            toolbar.ApplyDomain(MapEditorEditDomain.GEOMETRY);
            toolbar.SetCompact(true);
            Assert.True(toolbar.GetCombinedMinimumSize().X < 1000);
        }
        finally
        {
            toolbar.Free();
        }
    }

    private static T Export<T>(GodotObject node, string name) =>
        Assert.IsType<T>(node.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node));

    [Theory]
    [InlineData(1280)]
    [InlineData(900)]
    public void HudCanSelectLayerAndRasterizeWithoutOpeningTheObjectBrowser(int width)
    {
        MapEditorHud hud = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/MapEditor/MapEditor.tscn").Instantiate<MapEditorHud>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(hud);
        try
        {
            hud.Size = new Vector2(width, 720);
            MapEditorWorkspace workspace = Workspace();
            Add(workspace, new MapEditorRectBrushShape(0, 0, 2, 2, 0), MapEditorLayer.SOLID);
            Add(workspace, new MapEditorRectBrushShape(2, 0, 2, 2, 0), MapEditorLayer.SOLID);
            hud.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
            MapEditorWorkspaceShell shell = Export<MapEditorWorkspaceShell>(hud, "_workspaceShell");
            OptionButton layer = Export<OptionButton>(shell.Toolbar, "_workingLayer");
            layer.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Assert.Equal(MapEditorLayer.SOLID, shell.Canvas.SelectedLayer);
            Export<Button>(shell.Toolbar, "_selectAll").EmitSignal(BaseButton.SignalName.Pressed);
            Assert.Equal(2, shell.Canvas.SelectedBrushIds.Count);
            hud.BrushRasterizeRequested += ids =>
            {
                MapEditorOperationResult result = workspace.RasterizeBrushes(ids.ToHashSet());
                Assert.True(result.Succeeded);
                hud.Apply(result.Update!);
            };
            Export<Button>(shell.Toolbar, "_rasterize").EmitSignal(BaseButton.SignalName.Pressed);
            MapEditorBrush raster = Assert.Single(workspace.Snapshot.BrushDocument!.Layers.Solid.Brushes);
            Assert.Equal(raster.Id, shell.Canvas.SelectedBrushId);
            Assert.IsType<MapEditorRasterMaterial>(raster.Material);
            shell.Canvas.SelectLayer(MapEditorLayer.DESTRUCTIBLE);
            Assert.Equal(2, layer.Selected);

            MapEditorStamp stamp = new(new(1), "Raster stamp", MapEditorStampGeometry.CreateTemplate(raster));
            shell.Canvas.SelectStamp(stamp);
            Assert.Equal(MapEditorLayer.DESTRUCTIBLE, shell.Canvas.SelectedLayer);
            ImmutableArray<MapEditorBrushDraft> placed = [];
            shell.Canvas.BrushBatchAddRequested += drafts => placed = drafts;
            shell.Canvas._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(40, 40),
            });
            shell.Canvas._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = new Vector2(40, 40),
            });
            Assert.Equal(MapEditorLayer.DESTRUCTIBLE, Assert.Single(placed).Layer);
            for (int i = 0; i < 3; i++)
            {
                fixture.RenderFrame();
            }
            Assert.True(Export<VBoxContainer>(shell, "_chrome").GetCombinedMinimumSize().X <= width,
                "The toolbar must fit the editor viewport width.");
        }
        finally
        {
            hud.Free();
            fixture.RenderFrame();
        }
    }

    private static MapEditorWorkspace Workspace()
    {
        using MemoryStream png = new();
        MapEditorPngEncoder.EncodeRgba(png, 1, 1, (_, row) => row.Clear());
        MapEditorLayerAsset asset = new(png.ToArray(), 1, 1);
        MapEditorWorkspace workspace = new("test", new MapManifest { Name = "Map", SuggestedPlayers = 2 },
            new MapEditorLayers(asset, asset, asset));
        Assert.True(workspace.InitializeBrushSource().Succeeded);
        return workspace;
    }

    private static void Add(MapEditorWorkspace workspace, MapEditorBrushShape shape,
        MapEditorLayer layer = MapEditorLayer.BACKGROUND, bool visible = true,
        bool missing = false, MapEditorColor? color = null)
    {
        MapEditorBrushMaterial material = missing
            ? new MapEditorTextureMaterial(MapEditorTextureReference.Project("missing.png"))
            : new MapEditorSolidColorMaterial(color ?? new MapEditorColor(200, 40, 20));
        Assert.True(workspace.AddBrush(new MapEditorBrushDraft("Shape", layer, shape, material,
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT, new(0, 0), 1, 1, 0), visible)).Succeeded);
    }

    private static ImmutableArray<MapEditorBrush> Brushes(MapEditorSnapshot snapshot) =>
        snapshot.BrushDocument!.Layers.Background.Brushes;

    private static HashSet<MapEditorBrushId> Ids(MapEditorSnapshot snapshot) =>
        Brushes(snapshot).Select(brush => brush.Id).ToHashSet();

    private static MapEditorLayerCompositor Compositor() =>
        new(new MapEditorTextureResolver(new UnavailableMapEditorTextureAccess()));

    private static MapEditorLayerAsset Bake(MapEditorSnapshot snapshot) =>
        Compositor().Compose(snapshot.BrushDocument!.Layers.Background, snapshot.Bounds).Baked!;
}
