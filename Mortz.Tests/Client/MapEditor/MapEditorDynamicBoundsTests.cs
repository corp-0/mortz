using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorDynamicBoundsTests
{
    [Fact]
    public void BrushEditsFitNegativeAndSparseAuthoringCoordinates()
    {
        MapEditorWorkspace workspace = SourceWorkspace();
        MapEditorBrushId id = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            Draft(new MapEditorRectBrushShape(-20, -10, 30, 20, 0))).Update!.Change).Id;

        Assert.Equal(new MapEditorMapBounds(-20, -10, 30, 20), workspace.Snapshot.Bounds);
        Assert.All(Enum.GetValues<MapEditorLayer>(), layer =>
            Assert.True(workspace.Snapshot.BrushDocument!.Layers.Get(layer).BakeDirty));

        Assert.True(workspace.ReplaceBrush(id,
            Draft(new MapEditorRectBrushShape(500, 700, 10, 12, 0))).Succeeded);

        Assert.Equal(new MapEditorMapBounds(500, 700, 10, 12), workspace.Snapshot.Bounds);
        Assert.True(workspace.Undo().Succeeded);
        Assert.Equal(new MapEditorMapBounds(-20, -10, 30, 20), workspace.Snapshot.Bounds);
    }

    [Fact]
    public void FitIncludesHiddenBrushesZonesAndSpawnPoints()
    {
        MapEditorWorkspace workspace = SourceWorkspace();
        workspace.AddBrush(Draft(new MapEditorRectBrushShape(-40, -20, 5, 5, 0)));
        workspace.AddZone(new MapEditorZoneDraft("zone", [],
            new CircleMapZoneShape(10, 10, 4), []));
        workspace.AddSpawn(new MapSpawnPoint(100, 200));

        Assert.Equal(new MapEditorMapBounds(-40, -20, 141, 221), workspace.Snapshot.Bounds);
    }

    [Fact]
    public void OversizedFitRemainsEditableButBlocksTextureCreation()
    {
        MapEditorWorkspace workspace = SourceWorkspace();

        MapEditorOperationResult result = workspace.AddBrush(
            Draft(new MapEditorRectBrushShape(-100, 20, 8193, 10, 0)));

        Assert.True(result.Succeeded);
        Assert.Equal(new MapEditorMapBounds(-100, 20, 8193, 10), workspace.Snapshot.Bounds);
        Assert.Contains(workspace.Snapshot.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("maximum size is 8192 x 8192", StringComparison.Ordinal));
        Assert.False(workspace.Snapshot.CanSave);
    }

    [Fact]
    public void RejectedBrushLeavesBoundsHistoryAndSnapshotUntouched()
    {
        MapEditorWorkspace workspace = SourceWorkspace();
        MapEditorSnapshot before = workspace.Snapshot;
        MapEditorBrushDraft invalid = Draft(new MapEditorRectBrushShape(-20, -20, 5, 5,
            float.NaN));

        MapEditorOperationResult result = workspace.AddBrush(invalid);

        Assert.False(result.Succeeded);
        Assert.IsType<MapEditorContentFailure>(result.Failure);
        Assert.Same(before, workspace.Snapshot);
        Assert.Equal(before.Bounds, workspace.Snapshot.Bounds);
        Assert.Equal(before.CanUndo, workspace.Snapshot.CanUndo);
    }

    [Fact]
    public void EmptySourceRetainsItsLastUsableBounds()
    {
        MapEditorWorkspace workspace = SourceWorkspace(width: 64, height: 48);

        Assert.Equal(new MapEditorMapBounds(0, 0, 64, 48), workspace.Snapshot.Bounds);
    }

    [Fact]
    public void RemovingLastObjectRetainsTheLastBoundsInsteadOfCreatingAZeroSizedMap()
    {
        MapEditorWorkspace workspace = SourceWorkspace();
        MapEditorBrushId id = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            Draft(new MapEditorRectBrushShape(-30, 40, 12, 9, 0))).Update!.Change).Id;
        MapEditorMapBounds fitted = workspace.Snapshot.Bounds;

        Assert.True(workspace.RemoveBrush(id).Succeeded);

        Assert.Equal(fitted, workspace.Snapshot.Bounds);
        Assert.Empty(workspace.Snapshot.BrushDocument!.Layers.Background.Brushes);
    }

    [Fact]
    public void PersistedOriginRestoresRuntimeEntitiesToAuthoringCoordinates()
    {
        MapEditorLayerAsset asset = new([1], 20, 10);
        MapEditorLayers layers = new(asset, asset, asset);
        MapEditorBrushDocument document = Document(layers) with
        {
            Origin = new MapEditorPoint(-50, 30),
        };
        MapManifest manifest = new()
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "zone",
                    Shape = new RectMapZoneShape(2, 3, 4, 5),
                },
            ],
            SpawnPoints = [new MapSpawnPoint(6, 7)],
        };
        ContentDefinition<MapManifest> definition = new("test", manifest,
            "/maps/test", "/maps/test/map.toml",
            new ContentPackDefinition(new ContentPackManifest("org.test", "Test", "1"),
                "/pack"));
        StoredMapStore store = new(new MapEditorStoredMap(definition, manifest, layers, document));

        MapEditorWorkspace workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(definition, store).Workspace);

        Assert.Equal(new MapEditorMapBounds(-48, 33, 5, 5), workspace.Snapshot.Bounds);
        Assert.All(Enum.GetValues<MapEditorLayer>(), layer =>
            Assert.True(workspace.Snapshot.BrushDocument!.Layers.Get(layer).BakeDirty));
        Assert.Equal(new RectMapZoneShape(-48, 33, 4, 5),
            Assert.Single(workspace.Snapshot.Zones).Shape);
        Assert.Equal(new MapSpawnPoint(-44, 37),
            Assert.Single(workspace.Snapshot.SpawnPoints).Value);
        Assert.Equal(new RectMapZoneShape(0, 0, 4, 5),
            Assert.Single(workspace.BuildManifest().Zones).Shape);
        Assert.Equal([new MapSpawnPoint(4, 4)], workspace.BuildManifest().SpawnPoints);
        Assert.True(workspace.Save().Succeeded);
        Assert.Equal(5, store.SavedWidth);
        Assert.Equal(5, store.SavedHeight);
        Assert.Equal(new MapEditorMapOrigin(-48, 33), store.SavedDocument!.Origin);
    }

    [Fact]
    public void EditorDocumentOriginRoundTripsDeterministically()
    {
        MapEditorLayerAsset asset = new([1], 4, 3);
        MapEditorLayers layers = new(asset, asset, asset);
        MapEditorBrushDocument document = Document(layers) with
        {
            Origin = new MapEditorPoint(-12, 45),
        };

        byte[] first = MapEditorDocumentJson.Serialize(document);
        MapEditorBrushDocument restored = MapEditorDocumentJson.Deserialize(first, layers);
        byte[] second = MapEditorDocumentJson.Serialize(restored);

        Assert.Equal(new MapEditorMapOrigin(-12, 45), restored.Origin);
        Assert.Equal(first, second);
    }

    private static MapEditorWorkspace SourceWorkspace(int width = 100, int height = 80)
    {
        MapEditorLayerAsset asset = new([1], width, height);
        MapEditorWorkspace workspace = new("test", new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        }, new MapEditorLayers(asset, asset, asset));
        Assert.True(workspace.InitializeBrushSource().Succeeded);
        return workspace;
    }

    private static MapEditorBrushDraft Draft(MapEditorBrushShape shape) => new(
        "brush", MapEditorLayer.BACKGROUND, shape,
        new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            shape switch
            {
                MapEditorRectBrushShape rect => new MapEditorPoint(rect.X, rect.Y),
                _ => default,
            }, 1, 1, 0),
        Visible: false);

    private static MapEditorBrushDocument Document(MapEditorLayers layers) => new(
        MapEditorBrushDocument.CURRENT_VERSION, 1,
        new MapEditorLayerSources(
            new MapEditorLayerSource([], layers.Background, false),
            new MapEditorLayerSource([], layers.Solid, false),
            new MapEditorLayerSource([], layers.Destructible, false)));

    private sealed class StoredMapStore(MapEditorStoredMap stored) : IMapEditorStore
    {
        public int SavedWidth { get; private set; }
        public int SavedHeight { get; private set; }
        public MapEditorBrushDocument? SavedDocument { get; private set; }

        public MapEditorStoreResult<MapEditorStoredMap> Load(
            ContentDefinition<MapManifest> definition) =>
            MapEditorStoreResult<MapEditorStoredMap>.Success(stored);

        public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
            ContentDefinition<MapManifest> definition, MapManifest manifest,
            MapEditorLayers layers, int width, int height,
            MapEditorBrushDocument? brushDocument)
        {
            SavedWidth = width;
            SavedHeight = height;
            SavedDocument = brushDocument;
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(
                definition with { Manifest = manifest });
        }
    }
}
