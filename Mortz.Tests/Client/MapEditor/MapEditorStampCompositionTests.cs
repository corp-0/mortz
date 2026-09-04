using System.Collections.Immutable;
using System.Reflection;
using Godot;
using Mortz.Client.MapEditor;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public class MapEditorStampCompositionTests(MortzGodotFixture fixture)
{
    [Fact]
    public void StampScenesResolveTheirExportedDependencies()
    {
        MapEditorStampDock dock = Instantiate<MapEditorStampDock>(
            "res://src/Shared/UI/MapEditor/MapEditorStampDock.tscn");
        MapEditorStampLibrary library = Instantiate<MapEditorStampLibrary>(
            "res://src/Shared/UI/MapEditor/MapEditorStampLibrary.tscn");
        MapEditorStampCard card = Instantiate<MapEditorStampCard>(
            "res://src/Shared/UI/MapEditor/MapEditorStampCard.tscn");
        MapEditorWorkspaceShell shell = Instantiate<MapEditorWorkspaceShell>(
            "res://src/Shared/UI/MapEditor/MapEditorWorkspaceShell.tscn");

        AssertExportsResolved(dock);
        AssertExportsResolved(library);
        AssertExportsResolved(card);
        AssertExportsResolved(shell);
        card._Ready();
        Assert.NotNull(dock.Library);

        dock.Free();
        library.Free();
        card.Free();
        shell.Free();
    }

    [Fact]
    public void StampCardDeleteButtonRequestsLibraryDeletion()
    {
        MapEditorStampCard card = Instantiate<MapEditorStampCard>(
            "res://src/Shared/UI/MapEditor/MapEditorStampCard.tscn");
        Button delete = Assert.IsType<Button>(typeof(MapEditorStampCard)
            .GetField("_deleteButton", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(card));
        bool requested = false;
        card.DeleteRequested += () => requested = true;
        card._Ready();

        delete.EmitSignal(BaseButton.SignalName.Pressed);

        Assert.True(requested);
        card.Free();
    }

    [Fact]
    public void LargeLibraryFitsTheDockAndScrollsToTheLastStampAfterResizing()
    {
        SubViewport viewport = new() { Size = new Vector2I(1000, 600) };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);
        try
        {
            MapEditorStampDock dock = Instantiate<MapEditorStampDock>(
                "res://src/Shared/UI/MapEditor/MapEditorStampDock.tscn");
            dock.Size = new Vector2(900, 272);
            viewport.AddChild(dock);
            MapEditorSnapshot snapshot = StampSnapshot(48);
            dock.Library.Apply(snapshot, null, null);
            SettleLayout();

            Assert.Equal(48 + 1, dock.Library.GetChildCount());
            Assert.Equal(272, dock.Size.Y);
            Assert.True(dock.Library.Columns >= 4);
            Assert.True(dock.GetVScrollBar().Visible);
            Assert.False(dock.GetHScrollBar().Visible);
            Assert.True(dock.GetVScrollBar().MaxValue > dock.GetVScrollBar().Page);
            Assert.True(dock.Library.Size.X <= dock.Size.X);

            viewport.PushInput(new InputEventMouseMotion { Position = new Vector2(40, 40) }, true);
            viewport.PushInput(new InputEventMouseButton
            {
                Position = new Vector2(40, 40),
                ButtonIndex = MouseButton.WheelDown,
                Pressed = true,
            }, true);
            SettleLayout();
            Assert.True(dock.ScrollVertical > 0);

            dock.Size = new Vector2(320, 220);
            SettleLayout();
            Assert.Equal(1, dock.Library.Columns);
            Assert.Equal(new Vector2(320, 220), dock.Size);
            Assert.True(dock.Library.Size.X <= dock.Size.X);

            MapEditorStampCard last = dock.Library.GetChildren().OfType<MapEditorStampCard>().Last();
            last.GrabFocus();
            SettleLayout();
            Assert.True(dock.ScrollVertical > 0);
            Assert.True(dock.GetGlobalRect().Encloses(last.GetGlobalRect()));

            MapEditorStamp? selected = null;
            dock.Library.StampSelected += stamp => selected = stamp;
            viewport.PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = true }, true);
            viewport.PushInput(new InputEventKey { Keycode = Key.Enter, Pressed = false }, true);
            Assert.Equal(snapshot.BrushDocument!.Stamps.Last(), selected);

            dock.Size = new Vector2(900, 272);
            SettleLayout();
            Assert.True(dock.Library.Columns >= 4);
            Assert.True(dock.Library.Size.X <= dock.Size.X);
        }
        finally
        {
            viewport.Free();
        }
    }

    [Theory]
    [InlineData(800, 500)]
    [InlineData(1280, 658)]
    [InlineData(1920, 1000)]
    public void WorkspaceKeepsStampsAboveStatusBarAndUsesSpaceFromClosedPanels(int width, int height)
    {
        SubViewport viewport = new() { Size = new Vector2I(width, height) };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);
        try
        {
            MapEditorWorkspaceShell shell = Instantiate<MapEditorWorkspaceShell>(
                "res://src/Shared/UI/MapEditor/MapEditorWorkspaceShell.tscn");
            shell.Size = new Vector2(width, height);
            viewport.AddChild(shell);
            shell.SetStampLibraryAvailable(true);
            shell.StampLibrary.Apply(StampSnapshot(48), null, null);
            shell.OpenStamps();
            SettleLayout();

            MapEditorStampDock dock = Assert.IsType<MapEditorStampDock>(typeof(MapEditorWorkspaceShell)
                .GetField("_stampDock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell));
            Assert.True(shell.GetGlobalRect().Encloses(dock.GetGlobalRect()));
            Assert.InRange(dock.GetGlobalRect().End.Y, height - 47, height - 45);
            Assert.InRange(dock.Size.Y, 1, MapEditorWorkspaceShell.STAMP_DOCK_HEIGHT);
            Assert.True(dock.GetVScrollBar().MaxValue > dock.GetVScrollBar().Page);
            Assert.True(dock.Library.Size.X <= dock.Size.X);

            shell.CloseDrawer();
            shell.CloseProperties();
            SettleLayout();
            Assert.InRange(dock.Size.X, width - 17, width - 15);
            Assert.True(dock.Library.Size.X <= dock.Size.X);

            shell.CloseStamps();
            SettleLayout();
            Assert.False(dock.Visible);
        }
        finally
        {
            viewport.Free();
        }
    }

    private void SettleLayout()
    {
        for (int frame = 0; frame < 4; frame++)
        {
            fixture.RenderFrame();
        }
    }

    private static MapEditorSnapshot StampSnapshot(int count)
    {
        using Image image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        MapEditorLayerAsset layer = new(image.SavePngToBuffer(), 32, 32);
        MapEditorLayers layers = new(layer, layer, layer);
        MapEditorBrushDraft brush = new("Cover", MapEditorLayer.SOLID,
            new MapEditorRectBrushShape(0, 0, 32, 32, 0),
            new MapEditorSolidColorMaterial(new MapEditorColor(100, 150, 200)),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                new MapEditorPoint(0, 0), 1, 1, 0));
        MapEditorBrushDocument document = new(MapEditorBrushDocument.CURRENT_VERSION, 1,
            new MapEditorLayerSources(new MapEditorLayerSource([], layer, false),
                new MapEditorLayerSource([], layer, false),
                new MapEditorLayerSource([], layer, false)), new MapEditorMapOrigin(0, 0))
        {
            Stamps = Enumerable.Range(1, count).Select(id => new MapEditorStamp(
                new MapEditorStampId(id), $"Station cover with a long name {id}", brush)).ToImmutableArray(),
        };
        return new MapEditorSnapshot("map", "Map", 1, [], [], layers, 32, 32, 0, 0, [],
            BrushDocument: document);
    }

    private static T Instantiate<T>(string path) where T : Node =>
        ResourceLoader.Load<PackedScene>(path).Instantiate<T>();

    private static void AssertExportsResolved<T>(T node) where T : Node
    {
        foreach (FieldInfo field in typeof(T)
                     .GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                BindingFlags.Public)
                     .Where(field => field.IsDefined(typeof(ExportAttribute))))
        {
            Assert.NotNull(field.GetValue(node));
        }
    }
}
