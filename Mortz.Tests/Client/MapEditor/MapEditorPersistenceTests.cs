using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorPersistenceTests
{
    [Fact]
    public void OpenInitializesCleanWorkspaceWithAllLayers()
    {
        FakeStore store = new();
        store.Loads.Enqueue(Map("loaded", [1], [2], [3]));

        MapEditorOpenResult result = MapEditorWorkspace.Open(Definition(), store);

        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(result.Workspace);
        Assert.IsType<MapEditorOpened>(result.Update?.Change);
        Assert.False(workspace.Snapshot.Dirty);
        Assert.Equal("loaded", workspace.Snapshot.Name);
        Assert.Equal([1], workspace.Snapshot.Layers.Background.Png.ToArray());
        Assert.Equal([2], workspace.Snapshot.Layers.Solid.Png.ToArray());
        Assert.Equal([3], workspace.Snapshot.Layers.Destructible.Png.ToArray());
    }

    [Fact]
    public void SuccessfulSaveWritesCurrentManifestAndBytesThenBecomesClean()
    {
        FakeStore store = OpenStore(out MapEditorWorkspace workspace);
        byte[] publicCopy = workspace.Snapshot.Layers.Background.Png.ToArray();
        publicCopy[0] = 99;
        workspace.AddSpawn(new MapSpawnPoint(4, 5));

        MapEditorOperationResult result = workspace.Save();

        Assert.True(result.Succeeded);
        Assert.IsType<MapEditorSaved>(result.Update?.Change);
        Assert.False(workspace.Snapshot.Dirty);
        Assert.Equal(workspace.Snapshot.Revision, workspace.Snapshot.SavedRevision);
        Assert.Equal(new MapSpawnPoint(4, 5), Assert.Single(store.SavedManifest!.SpawnPoints));
        Assert.Equal([1], store.SavedLayers!.Background.Png.ToArray());
        Assert.Equal([3], store.SavedLayers!.Destructible.Png.ToArray());
    }

    [Fact]
    public void FailedSavePreservesDirtySnapshotAndValidationDiagnostics()
    {
        FakeStore store = OpenStore(out MapEditorWorkspace workspace);
        workspace.AddSpawn(new MapSpawnPoint(10, 2));
        MapEditorSnapshot before = workspace.Snapshot;
        ContentDiagnostic diagnostic = new(ContentDiagnosticSeverity.ERROR,
            "map.toml", "writer rejected it");
        store.SaveFailure = new MapEditorContentFailure([diagnostic]);

        MapEditorOperationResult result = workspace.Save();

        MapEditorContentFailure failure = Assert.IsType<MapEditorContentFailure>(result.Failure);
        Assert.Equal(diagnostic, Assert.Single(failure.Diagnostics));
        Assert.Same(before, workspace.Snapshot);
        Assert.True(workspace.Snapshot.Dirty);
    }

    [Fact]
    public void ReloadFailurePreservesWorkingStateAndTypedDiagnostics()
    {
        FakeStore store = OpenStore(out MapEditorWorkspace workspace);
        workspace.AddZone(Draft("working"));
        MapEditorSnapshot before = workspace.Snapshot;
        ContentDiagnostic diagnostic = new(ContentDiagnosticSeverity.ERROR,
            "map.toml", "cannot reload");
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Failed(
            new MapEditorContentFailure([diagnostic])));

        MapEditorOperationResult result = workspace.Reload();

        MapEditorContentFailure failure = Assert.IsType<MapEditorContentFailure>(result.Failure);
        Assert.Equal(diagnostic, Assert.Single(failure.Diagnostics));
        Assert.Same(before, workspace.Snapshot);
    }

    [Fact]
    public void SuccessfulReloadReplacesContentReallocatesIdsAndBecomesClean()
    {
        FakeStore store = OpenStore(out MapEditorWorkspace workspace,
            new MapManifest { Name = "old", SuggestedPlayers = 1, Zones = [Zone("old")] });
        MapEditorZoneId oldId = workspace.Snapshot.Zones[0].Id;
        workspace.AddSpawn(new MapSpawnPoint(2, 2));
        store.Loads.Enqueue(Map("new", [4], [5], [6],
            new MapManifest { Name = "new", SuggestedPlayers = 2, Zones = [Zone("new")] }));

        MapEditorOperationResult result = workspace.Reload();

        Assert.IsType<MapEditorReloaded>(result.Update?.Change);
        Assert.Equal("new", workspace.Snapshot.Name);
        Assert.NotEqual(oldId, workspace.Snapshot.Zones[0].Id);
        Assert.Empty(workspace.Snapshot.SpawnPoints);
        Assert.False(workspace.Snapshot.Dirty);
        Assert.Equal(workspace.Snapshot.Revision, workspace.Snapshot.SavedRevision);
    }

    private static FakeStore OpenStore(out MapEditorWorkspace workspace,
        MapManifest? manifest = null)
    {
        FakeStore store = new();
        store.Loads.Enqueue(Map("map", [1], [2], [3], manifest));
        workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(Definition(), store).Workspace);
        return store;
    }

    private static MapEditorStoreResult<MapEditorStoredMap> Map(string name,
        byte[] background, byte[] solid, byte[] destructible, MapManifest? manifest = null)
    {
        manifest ??= new MapManifest { Name = name, SuggestedPlayers = 1 };
        MapEditorLayers layers = new(
            new MapEditorLayerAsset(background, 10, 8),
            new MapEditorLayerAsset(solid, 10, 8),
            new MapEditorLayerAsset(destructible, 10, 8));
        return MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(Definition(manifest), manifest, layers));
    }

    private static ContentDefinition<MapManifest> Definition(MapManifest? manifest = null) =>
        new("test", manifest ?? new MapManifest { Name = "stale", SuggestedPlayers = 1 },
            "/pack/maps/test", "/pack/maps/test/map.toml",
            new ContentPackDefinition(new ContentPackManifest("org.test", "Test", "1"),
                "/pack"));

    private static MapZoneDef Zone(string name) => new()
    {
        Name = name,
        Shape = new CircleMapZoneShape(4, 4, 2),
    };

    private static MapEditorZoneDraft Draft(string name) => new(name, [],
        new CircleMapZoneShape(4, 4, 2), []);

    private sealed class FakeStore : IMapEditorStore
    {
        public Queue<MapEditorStoreResult<MapEditorStoredMap>> Loads { get; } = new();
        public MapEditorOperationFailure? SaveFailure { get; set; }
        public MapManifest? SavedManifest { get; private set; }
        public MapEditorLayers? SavedLayers { get; private set; }
        public MapEditorBrushDocument? SavedBrushDocument { get; private set; }

        public MapEditorStoreResult<MapEditorStoredMap> Load(
            ContentDefinition<MapManifest> definition) => Loads.Dequeue();

        public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
            ContentDefinition<MapManifest> definition, MapManifest manifest,
            MapEditorLayers layers, int width, int height,
            MapEditorBrushDocument? brushDocument)
        {
            SavedManifest = manifest;
            SavedLayers = layers;
            SavedBrushDocument = brushDocument;
            return SaveFailure == null
                ? MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(
                    definition with { Manifest = manifest })
                : MapEditorStoreResult<ContentDefinition<MapManifest>>.Failed(SaveFailure);
        }
    }
}
