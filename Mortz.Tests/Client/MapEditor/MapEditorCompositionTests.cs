using System.Reflection;
using Godot;
using Mortz.Client.MapEditor;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorCompositionTests
{
    [Fact]
    public void ScreenContainsFlowAndBothSeparateHuds()
    {
        MapEditorScreen screen = Instantiate<MapEditorScreen>(
            "res://src/Shared/Scenes/MapEditor/MapEditor.tscn");

        Assert.IsType<MapEditorFlow>(screen.GetNode("Flow"));
        Assert.DoesNotContain(screen.GetChildren(), child => child.Name == "Editor");
        Assert.IsType<MapEditorFlowHud>(screen.GetNode("Hud/MapEditorFlowHud"));
        Assert.IsType<MapEditorHud>(screen.GetNode("Hud/MapEditorHud"));
        Assert.True(screen.GetNode<Control>("Hud/MapEditorFlowHud").Visible);
        Assert.False(screen.GetNode<Control>("Hud/MapEditorHud").Visible);
        screen.Free();
    }

    [Fact]
    public void ScreenOwnsWorkspaceAndStoreWhileHudOwnsNeither()
    {
        FieldInfo[] screenFields = typeof(MapEditorScreen).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo[] hudFields = typeof(MapEditorHud).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.Contains(screenFields, field => field.FieldType == typeof(MapEditorWorkspace));
        Assert.Contains(screenFields, field => field.FieldType == typeof(IMapEditorStore));
        Assert.DoesNotContain(hudFields, field => field.FieldType == typeof(MapEditorWorkspace));
        Assert.DoesNotContain(hudFields, field => field.FieldType == typeof(IMapEditorStore));
    }

    [Theory]
    [InlineData("res://src/Shared/Scenes/MapEditor/MapEditor.tscn", typeof(MapEditorScreen))]
    [InlineData("res://src/Shared/UI/MapEditor/MapEditorFlow.tscn", typeof(MapEditorFlowHud))]
    [InlineData("res://src/Shared/UI/MapEditor/MapEditor.tscn", typeof(MapEditorHud))]
    public void SceneExportsAreWired(string path, Type type)
    {
        Node node = ResourceLoader.Load<PackedScene>(path).Instantiate();

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance |
                     BindingFlags.NonPublic | BindingFlags.Public)
                     .Where(field => field.IsDefined(typeof(ExportAttribute))))
        {
            Assert.NotNull(field.GetValue(node));
        }
        node.Free();
    }

    [Fact]
    public void EditorHudNoLongerContainsAMapPicker()
    {
        MapEditorHud hud = Instantiate<MapEditorHud>(
            "res://src/Shared/UI/MapEditor/MapEditor.tscn");

        Assert.Null(hud.FindChild("MapPicker", recursive: true, owned: false));
        hud.Free();
    }

    [Fact]
    public void LayerImagesAndGuideVisibilityUseSeparatePanels()
    {
        MapEditorHud hud = Instantiate<MapEditorHud>(
            "res://src/Shared/UI/MapEditor/MapEditor.tscn");

        Assert.NotNull(hud.GetNode("Layout/LayerDock"));
        Assert.NotNull(hud.GetNode("Layout/Sidebar/Guides"));
        Assert.NotNull(hud.GetNode("LayerFileDialog"));
        Assert.False(hud.GetNode<Control>("Layout/Sidebar/Inspector").Visible);
        hud.Free();
    }

    private static T Instantiate<T>(string path) where T : Node =>
        ResourceLoader.Load<PackedScene>(path).Instantiate<T>();
}
