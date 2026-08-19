using System.Reflection;
using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorScreenTests
{
    [Fact]
    public void CleanBackReturnsToFlowImmediately()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Stored("original"));
        using Fixture fixture = new(store);
        fixture.Open();

        fixture.Invoke("RequestBack");

        Assert.Null(fixture.Workspace);
        Assert.False(fixture.EditorHud.Visible);
        Assert.Equal(MapEditorPendingAction.NONE, fixture.PendingAction);
    }

    [Fact]
    public void CleanReloadExecutesImmediately()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Stored("original"));
        store.Loads.Enqueue(Stored("reloaded"));
        using Fixture fixture = new(store);
        fixture.Open();

        fixture.Invoke("RequestReload");

        Assert.Equal("reloaded", fixture.Workspace?.Snapshot.Name);
        Assert.Equal(2, store.LoadCount);
        Assert.Equal(MapEditorPendingAction.NONE, fixture.PendingAction);
    }

    [Fact]
    public void DirtyBackWaitsForConfirmationAndCancellationClearsTheRequest()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Stored("original"));
        using Fixture fixture = new(store);
        fixture.Open();
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(fixture.Workspace);
        workspace.AddSpawn(new MapSpawnPoint(4, 4));

        fixture.Invoke("RequestBack");

        Assert.Same(workspace, fixture.Workspace);
        Assert.Equal(MapEditorPendingAction.BACK, fixture.PendingAction);
        Assert.True(fixture.EditorHud.Visible);

        fixture.Invoke("CancelDiscard");

        Assert.Same(workspace, fixture.Workspace);
        Assert.Equal(MapEditorPendingAction.NONE, fixture.PendingAction);

        fixture.Invoke("RequestBack");
        fixture.Invoke("ConfirmDiscard");

        Assert.Null(fixture.Workspace);
        Assert.False(fixture.EditorHud.Visible);
        Assert.Equal(MapEditorPendingAction.NONE, fixture.PendingAction);
    }

    [Fact]
    public void FailedConfirmedReloadPreservesDirtyWorkspace()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Stored("original"));
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Failed(
            new MapEditorIoFailure("reload unavailable")));
        using Fixture fixture = new(store);
        fixture.Open();
        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(fixture.Workspace);
        workspace.AddSpawn(new MapSpawnPoint(4, 4));
        MapEditorSnapshot before = workspace.Snapshot;

        fixture.Invoke("RequestReload");

        Assert.Equal(MapEditorPendingAction.RELOAD, fixture.PendingAction);
        Assert.Equal(1, store.LoadCount);

        fixture.Invoke("ConfirmDiscard");

        Assert.Same(workspace, fixture.Workspace);
        Assert.Same(before, workspace.Snapshot);
        Assert.True(workspace.Snapshot.Dirty);
        Assert.Equal(2, store.LoadCount);
        Assert.Equal(MapEditorPendingAction.NONE, fixture.PendingAction);
        Assert.True(fixture.EditorHud.Visible);
    }

    [Fact]
    public void ScreenDoesNotSaveCleanOrInvalidSnapshots()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Stored("original"));
        using Fixture fixture = new(store);
        fixture.Open();

        fixture.Invoke("Save");
        Assert.Equal(0, store.SaveCount);

        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(fixture.Workspace);
        workspace.AddSpawn(new MapSpawnPoint(30, 4));
        Assert.True(workspace.Snapshot.Dirty);
        Assert.False(workspace.Snapshot.CanSave);

        fixture.Invoke("Save");

        Assert.Equal(0, store.SaveCount);
    }

    private static MapEditorStoreResult<MapEditorStoredMap> Stored(string name)
    {
        Image image = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        MapEditorLayerAsset asset = new(image.SavePngToBuffer(), 20, 20);
        MapManifest manifest = new() { Name = name, SuggestedPlayers = 1 };
        ContentDefinition<MapManifest> definition = Definition(manifest);
        return MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(definition, manifest,
                new MapEditorLayers(asset, asset, asset)));
    }

    private static ContentDefinition<MapManifest> Definition(MapManifest? manifest = null) =>
        new("test", manifest ?? new MapManifest { Name = "stale", SuggestedPlayers = 1 },
            "/pack/maps/test", "/pack/maps/test/map.toml",
            new ContentPackDefinition(new ContentPackManifest("org.test", "Test", "1"),
                "/pack"));

    private sealed class Fixture : IDisposable
    {
        private static readonly BindingFlags _fields =
            BindingFlags.Instance | BindingFlags.NonPublic;

        public Fixture(IMapEditorStore store)
        {
            Screen = ResourceLoader.Load<PackedScene>(
                "res://src/Shared/Scenes/MapEditor/MapEditor.tscn")
                .Instantiate<MapEditorScreen>();
            typeof(MapEditorScreen).GetField("_store", _fields)!.SetValue(Screen, store);
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(Screen);
        }

        public MapEditorScreen Screen { get; }
        public MapEditorHud EditorHud => Screen.GetNode<MapEditorHud>("Hud/MapEditorHud");
        public MapEditorWorkspace? Workspace =>
            (MapEditorWorkspace?)typeof(MapEditorScreen).GetField("_workspace", _fields)!
                .GetValue(Screen);
        public MapEditorPendingAction PendingAction =>
            (MapEditorPendingAction)typeof(MapEditorScreen).GetField("_pendingAction", _fields)!
                .GetValue(Screen)!;

        public void Open() => Invoke("OpenEditor", Definition());

        public void Invoke(string method, params object[] arguments) =>
            typeof(MapEditorScreen).GetMethod(method, _fields)!.Invoke(Screen, arguments);

        public void Dispose() => Screen.Free();
    }

    private sealed class FakeStore : IMapEditorStore
    {
        public Queue<MapEditorStoreResult<MapEditorStoredMap>> Loads { get; } = new();
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public MapEditorStoreResult<MapEditorStoredMap> Load(
            ContentDefinition<MapManifest> definition)
        {
            LoadCount++;
            return Loads.Dequeue();
        }

        public MapEditorStoreResult<MapEditorLayerAsset> LoadLayer(
            string? path, int expectedWidth, int expectedHeight) =>
            MapEditorStoreResult<MapEditorLayerAsset>.Failed(
                new MapEditorIoFailure("not configured"));

        public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
            ContentDefinition<MapManifest> definition, MapManifest manifest,
            MapEditorLayers layers, int width, int height)
        {
            SaveCount++;
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(definition);
        }
    }
}
