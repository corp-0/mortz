using System.Reflection;
using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorHudTests
{
    [Fact]
    public void ApplyingSnapshotDoesNotEmitEditIntents()
    {
        using Fixture fixture = new();
        int edits = 0;
        fixture.Hud.ZoneAddRequested += _ => edits++;
        fixture.Hud.ZoneReplaceRequested += (_, _) => edits++;
        fixture.Hud.ZoneRemoveRequested += _ => edits++;
        fixture.Hud.SpawnAddRequested += _ => edits++;
        fixture.Hud.SpawnReplaceRequested += (_, _) => edits++;
        fixture.Hud.SpawnRemoveRequested += _ => edits++;
        fixture.Hud.LayerReplaceRequested += (_, _) => edits++;

        fixture.Hud.Apply(Update(Snapshot(), new MapEditorOpened()));

        Assert.Equal(0, edits);
    }

    [Fact]
    public void InspectorEditUsesSelectedStableId()
    {
        using Fixture fixture = new();
        MapEditorZoneId expected = new(42);
        MapEditorZoneId? actual = null;
        MapEditorZoneDraft? replacement = null;
        MapEditorSnapshot snapshot = Snapshot() with
        {
            Zones = [new MapEditorZone(expected, "old", [],
                new RectMapZoneShape(1, 2, 3, 4), [])],
        };
        fixture.Hud.ZoneReplaceRequested += (id, draft) =>
        {
            actual = id;
            replacement = draft;
        };
        fixture.Hud.Apply(Update(snapshot, new MapEditorOpened()));
        fixture.Canvas.Select(expected);

        fixture.Name.Text = "changed";
        fixture.Name.EmitSignal(LineEdit.SignalName.TextChanged, fixture.Name.Text);

        Assert.Equal(expected, actual);
        Assert.Equal("changed", replacement?.Name);
    }

    [Fact]
    public void DeleteEmitsIntentWithoutChangingSelection()
    {
        using Fixture fixture = new();
        MapEditorZoneId expected = new(71);
        MapEditorZoneId? removed = null;
        MapEditorSnapshot snapshot = Snapshot() with
        {
            Zones = [new MapEditorZone(expected, "zone", [],
                new CircleMapZoneShape(2, 3, 4), [])],
        };
        fixture.Hud.ZoneRemoveRequested += id => removed = id;
        fixture.Hud.Apply(Update(snapshot, new MapEditorOpened()));
        fixture.Canvas.Select(expected);

        fixture.Hud.OnDeletePressed();

        Assert.Equal(expected, removed);
        Assert.Equal(expected, fixture.Canvas.SelectedZoneId);
    }

    [Fact]
    public void SaveEnablementUsesSnapshotCanSave()
    {
        using Fixture fixture = new();
        MapEditorSnapshot snapshot = Snapshot();
        fixture.Hud.Apply(Update(snapshot, new MapEditorOpened()));
        Assert.True(fixture.Save.Disabled);

        fixture.Hud.Apply(Update(snapshot with { Revision = 1 },
            new MapEditorZoneReplaced(new MapEditorZoneId(1))));
        Assert.False(fixture.Save.Disabled);

        ContentDiagnostic error = new(ContentDiagnosticSeverity.ERROR,
            "map.toml", "A zone name is duplicated.");
        fixture.Hud.Apply(Update(snapshot with { Revision = 2, Diagnostics = [error] },
            new MapEditorZoneReplaced(new MapEditorZoneId(1))));
        Assert.True(fixture.Save.Disabled);
    }

    [Fact]
    public void SaveShortcutOnlyEmitsForDirtyValidSnapshot()
    {
        using Fixture fixture = new();
        int requests = 0;
        fixture.Hud.SaveRequested += () => requests++;
        InputEventKey shortcut = new()
        {
            Pressed = true,
            CtrlPressed = true,
            Keycode = Key.S,
        };

        fixture.Hud.Apply(Update(Snapshot(), new MapEditorOpened()));
        fixture.Hud._UnhandledInput(shortcut);

        ContentDiagnostic error = new(ContentDiagnosticSeverity.ERROR,
            "map.toml", "The map is invalid.");
        fixture.Hud.Apply(Update(Snapshot() with { Revision = 1, Diagnostics = [error] },
            new MapEditorZoneReplaced(new MapEditorZoneId(1))));
        fixture.Hud._UnhandledInput(shortcut);

        fixture.Hud.Apply(Update(Snapshot() with { Revision = 1 },
            new MapEditorZoneReplaced(new MapEditorZoneId(1))));
        fixture.Hud._UnhandledInput(shortcut);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void DiagnosticsAndOperationErrorsArePresented()
    {
        using Fixture fixture = new();
        ContentDiagnostic diagnostic = new(ContentDiagnosticSeverity.ERROR,
            "map.toml", "Zone is outside the map.");

        fixture.Hud.Apply(Update(Snapshot() with { Diagnostics = [diagnostic] },
            new MapEditorOpened()));

        Assert.Contains(diagnostic.Message, fixture.Status.Text);

        fixture.Hud.ShowStatus(new MapEditorStatus("Save failed: disk full", true));

        Assert.Equal("Save failed: disk full", fixture.Status.Text);
        Assert.Equal("Save failed: disk full", fixture.ErrorDialog.DialogText);
    }

    [Fact]
    public void HudHasNoWorkspaceDependency()
    {
        FieldInfo[] fields = typeof(MapEditorHud).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, field => field.FieldType == typeof(MapEditorWorkspace));
        Assert.DoesNotContain(typeof(MapEditorHud).GetProperties(BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic), property =>
            property.PropertyType == typeof(MapEditorWorkspace));
    }

    private static MapEditorUpdate Update(MapEditorSnapshot snapshot, MapEditorChange change) =>
        new(snapshot, change);

    private static MapEditorSnapshot Snapshot()
    {
        Image image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        image.Fill(Colors.Black);
        MapEditorLayerAsset asset = new(image.SavePngToBuffer(), 2, 2);
        return new MapEditorSnapshot("test", "Test", 2, [], [],
            new MapEditorLayers(asset, asset, asset), 2, 2, 0, 0, []);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Hud = ResourceLoader.Load<PackedScene>(
                "res://src/Shared/UI/MapEditor/MapEditor.tscn").Instantiate<MapEditorHud>();
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(Hud);
        }

        public MapEditorHud Hud { get; }
        public MapEditorCanvas Canvas => Hud.GetNode<MapEditorCanvas>("Canvas");
        public LineEdit Name => Hud.GetNode<LineEdit>(
            "Layout/Sidebar/Inspector/Margin/Scroll/Column/Fields/Name");
        public Button Save => Hud.GetNode<Button>("Layout/TopBar/Margin/Row/Save");
        public Label Status => Hud.GetNode<Label>("Layout/TopBar/Margin/Row/Status");
        public AcceptDialog ErrorDialog => Hud.GetNode<AcceptDialog>("ErrorDialog");

        public void Dispose() => Hud.Free();
    }
}
