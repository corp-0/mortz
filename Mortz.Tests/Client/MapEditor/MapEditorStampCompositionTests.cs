using System.Reflection;
using Godot;
using Mortz.Client.MapEditor;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public class MapEditorStampCompositionTests
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
        dock._Ready();
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
