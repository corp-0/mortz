using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorBrushPersistenceTests
{
    [Fact]
    public void RasterOnlyWorkspaceDisablesBrushEditingButKeepsGameplayEditing()
    {
        FakeStore store = Open(out MapEditorWorkspace workspace);

        MapEditorOperationResult brush = workspace.AddBrush(Draft("blocked"));
        workspace.AddSpawn(new MapSpawnPoint(2, 3));

        Assert.Equal(MapEditorRasterSourceStatus.OBSOLETE, workspace.Snapshot.SourceStatus);
        Assert.False(workspace.Snapshot.CanEditBrushes);
        Assert.IsType<MapEditorBrushEditingUnavailableFailure>(brush.Failure);
        Assert.Single(workspace.Snapshot.SpawnPoints);
        Assert.Null(store.SavedDocument);
    }

    [Fact]
    public void EmptyDocumentStartsWithBrushEditingEnabledAndCleanLayers()
    {
        MapEditorLayers layers = Layers();

        MapEditorBrushDocument document = MapEditorBrushDocument.Empty(layers);
        Open(out MapEditorWorkspace workspace, document);

        Assert.True(workspace.Snapshot.CanEditBrushes);
        Assert.All(Enum.GetValues<MapEditorLayer>(), layer =>
        {
            MapEditorLayerSource source = workspace.Snapshot.BrushDocument!.Layers.Get(layer);
            Assert.Empty(source.Brushes);
            Assert.False(source.BakeDirty);
        });
    }

    [Fact]
    public void InitializationIsInMemoryDirtyAndCanBeCancelledUndoneAndRedone()
    {
        Open(out MapEditorWorkspace workspace);

        MapEditorOperationResult initialized = workspace.InitializeBrushSource();

        Assert.IsType<MapEditorBrushSourceInitialized>(initialized.Update?.Change);
        Assert.Equal(MapEditorRasterSourceStatus.BRUSH_SOURCE, workspace.Snapshot.SourceStatus);
        Assert.All(Enum.GetValues<MapEditorLayer>(), layer =>
            Assert.True(workspace.Snapshot.BrushDocument!.Layers.Get(layer).BakeDirty));
        Assert.True(workspace.Snapshot.CanSave);

        Assert.IsType<MapEditorUndoApplied>(workspace.CancelBrushSourceInitialization().Update?.Change);
        Assert.Equal(MapEditorRasterSourceStatus.OBSOLETE, workspace.Snapshot.SourceStatus);
        Assert.False(workspace.Snapshot.Dirty);
        Assert.False(workspace.Snapshot.CanSave);
        Assert.IsType<MapEditorRedoApplied>(workspace.Redo().Update?.Change);
        Assert.Equal(MapEditorRasterSourceStatus.BRUSH_SOURCE, workspace.Snapshot.SourceStatus);
        Assert.True(workspace.Snapshot.Dirty);
    }

    [Fact]
    public void EditorDocumentRoundTripsDeterministicallyAndIgnoresUnknownProperties()
    {
        MapEditorLayers baked = Layers();
        MapEditorBrushDocument document = Document(baked,
            Brush(9, "bottom", MapEditorLayer.BACKGROUND),
            Brush(4, "top", MapEditorLayer.BACKGROUND) with
            {
                Shape = new MapEditorPolygonBrushShape([
                    new MapEditorPoint(1, 1), new MapEditorPoint(6, 1),
                    new MapEditorPoint(3, 5),
                ]),
                Material = new MapEditorSolidColorMaterial(
                    new MapEditorColor(0x12, 0x34, 0x56, 0x78)),
            });
        document = document with
        {
            NextStampId = 2,
            Stamps = [new MapEditorStamp(new MapEditorStampId(1), "saved bottom",
                MapEditorStampGeometry.CreateTemplate(document.Layers.Background.Brushes[0]))],
        };

        byte[] first = MapEditorDocumentJson.Serialize(document);
        string serialized = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain("clearColor", serialized);
        Assert.Contains("\"source\": \"project\"", serialized);
        Assert.Contains("\"path\": \"texture.png\"", serialized);
        Assert.Contains("\"kind\": \"COLOR\"", serialized);
        Assert.Contains("\"rgba\": [", serialized);
        Assert.DoesNotContain("resourceUid", serialized);
        Assert.DoesNotContain("resourcePath", serialized);
        string withUnknown = serialized.Replace(
            "\"nextBrushId\": 10,", "\"nextBrushId\": 10,\n  \"futureHint\": true,");
        MapEditorBrushDocument parsed = MapEditorDocumentJson.Deserialize(
            Encoding.UTF8.GetBytes(withUnknown), baked);
        byte[] second = MapEditorDocumentJson.Serialize(parsed);

        Assert.Equal(first, second);
        Assert.Equal([9L, 4L], parsed.Layers.Background.Brushes.Select(brush => brush.Id.Value));
        Assert.Equal(["bottom", "top"], parsed.Layers.Background.Brushes.Select(brush => brush.Name));
        Assert.Equal(new MapEditorSolidColorMaterial(new MapEditorColor(0x12, 0x34, 0x56, 0x78)),
            parsed.Layers.Background.Brushes[1].Material);
        MapEditorStamp stamp = Assert.Single(parsed.Stamps);
        Assert.Equal("saved bottom", stamp.Name);
        Assert.Equal(new MapEditorRectBrushShape(0, 0, 3, 4, 0), stamp.Brush.Shape);
    }

    [Fact]
    public void FutureVersionIsRejectedWithSpecificVersionFailure()
    {
        byte[] json = MapEditorDocumentJson.Serialize(Document(Layers()));
        int futureVersion = MapEditorBrushDocument.CURRENT_VERSION + 1;
        string future = Encoding.UTF8.GetString(json).Replace(
            $"\"version\": {MapEditorBrushDocument.CURRENT_VERSION}",
            $"\"version\": {futureVersion}");

        MapEditorDocumentVersionException failure = Assert.Throws<MapEditorDocumentVersionException>(() =>
            MapEditorDocumentJson.Deserialize(Encoding.UTF8.GetBytes(future), Layers()));

        Assert.Equal(futureVersion, failure.Version);
    }

    [Fact]
    public void VersionOneDocumentLoadsWithAnEmptyStampLibrary()
    {
        JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(
            MapEditorDocumentJson.Serialize(Document(Layers()))));
        root["version"] = 1;
        root.Remove("nextStampId");
        root.Remove("stamps");

        MapEditorBrushDocument loaded = MapEditorDocumentJson.Deserialize(
            Encoding.UTF8.GetBytes(root.ToJsonString()), Layers());

        Assert.Equal(MapEditorBrushDocument.CURRENT_VERSION, loaded.Version);
        Assert.Equal(1, loaded.NextStampId);
        Assert.Empty(loaded.Stamps);
    }

    [Fact]
    public void SavingBrushAsStampNormalizesItWithoutDirtyingBakedLayers()
    {
        MapEditorBrush brush = Brush(1, "crate", MapEditorLayer.BACKGROUND) with
        {
            Shape = new MapEditorRectBrushShape(1, 2, 3, 4, 0),
            Projection = new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                new MapEditorPoint(5, 6), 1, 1, 0),
        };
        Open(out MapEditorWorkspace workspace, Document(Layers(), brush));

        MapEditorOperationResult result = workspace.SaveStamp(brush.Id);

        MapEditorStampSaved saved = Assert.IsType<MapEditorStampSaved>(result.Update?.Change);
        MapEditorStamp stamp = Assert.Single(workspace.Snapshot.BrushDocument!.Stamps);
        Assert.Equal(saved.Id, stamp.Id);
        Assert.Equal("crate", stamp.Name);
        Assert.Equal(new MapEditorRectBrushShape(0, 0, 3, 4, 0), stamp.Brush.Shape);
        Assert.Equal(new MapEditorPoint(4, 4), stamp.Brush.Projection.Origin);
        Assert.All(Enum.GetValues<MapEditorLayer>(), layer =>
            Assert.False(workspace.Snapshot.BrushDocument.Layers.Get(layer).BakeDirty));
        Assert.True(workspace.Snapshot.Dirty);

        workspace.Undo();
        Assert.Empty(workspace.Snapshot.BrushDocument.Stamps);
        workspace.Redo();
        Assert.Single(workspace.Snapshot.BrushDocument.Stamps);
    }

    [Fact]
    public void RemovingStampIsUndoableAndDoesNotRemovePaintedGeometry()
    {
        MapEditorBrush brush = Brush(1, "crate", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace workspace, Document(Layers(), brush));
        MapEditorStampSaved saved = Assert.IsType<MapEditorStampSaved>(
            workspace.SaveStamp(brush.Id).Update?.Change);

        MapEditorOperationResult result = workspace.RemoveStamp(saved.Id);

        Assert.IsType<MapEditorStampRemoved>(result.Update?.Change);
        Assert.Empty(workspace.Snapshot.BrushDocument!.Stamps);
        Assert.Single(workspace.Snapshot.BrushDocument.Layers.Background.Brushes);

        workspace.Undo();

        Assert.Single(workspace.Snapshot.BrushDocument.Stamps);
    }

    [Fact]
    public void StampPlacementTranslatesShapeAndProjectionFromItsAnchor()
    {
        MapEditorStamp stamp = new(new MapEditorStampId(1), "triangle",
            new MapEditorBrushDraft("triangle", MapEditorLayer.SOLID,
                new MapEditorPolygonBrushShape([
                    new MapEditorPoint(0, 0), new MapEditorPoint(8, 0),
                    new MapEditorPoint(4, 6),
                ]),
                new MapEditorSolidColorMaterial(new MapEditorColor(1, 2, 3)),
                new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                    new MapEditorPoint(2, 3), 1, 1, 0)));

        MapEditorBrushDraft placed = MapEditorStampGeometry.Place(stamp,
            new MapEditorPoint(32, 64), "triangle 2");

        Assert.Equal("triangle 2", placed.Name);
        Assert.Equal([
            new MapEditorPoint(32, 64), new MapEditorPoint(40, 64),
            new MapEditorPoint(36, 70),
        ], Assert.IsType<MapEditorPolygonBrushShape>(placed.Shape).Vertices);
        Assert.Equal(new MapEditorPoint(34, 67), placed.Projection.Origin);
    }

    [Fact]
    public void StampStrokeAddsAllBrushesAsOneHistoryEntry()
    {
        Open(out MapEditorWorkspace workspace, Document(Layers()));

        MapEditorOperationResult result = workspace.AddBrushes([
            Draft("stamp 1"),
            Draft("stamp 2") with
            {
                Shape = new MapEditorRectBrushShape(32, 0, 3, 4, 0),
            },
        ]);

        MapEditorBrushesAdded added = Assert.IsType<MapEditorBrushesAdded>(
            result.Update?.Change);
        Assert.Equal([new MapEditorBrushId(1), new MapEditorBrushId(2)], added.Ids);
        Assert.Equal(2, workspace.Snapshot.BrushDocument!.Layers.Background.Brushes.Length);
        Assert.Equal(1, workspace.Snapshot.Revision);

        workspace.Undo();

        Assert.Empty(workspace.Snapshot.BrushDocument.Layers.Background.Brushes);
    }

    [Fact]
    public void StampEraseRemovesAllPaintedBrushesAsOneHistoryEntry()
    {
        Open(out MapEditorWorkspace workspace, Document(Layers(),
            Brush(1, "keep", MapEditorLayer.BACKGROUND),
            Brush(2, "erase first", MapEditorLayer.BACKGROUND),
            Brush(3, "erase second", MapEditorLayer.BACKGROUND)));

        MapEditorOperationResult result = workspace.RemoveBrushes(
            new HashSet<MapEditorBrushId> { new(2), new(3) });

        MapEditorBrushesRemoved removed = Assert.IsType<MapEditorBrushesRemoved>(
            result.Update?.Change);
        Assert.Equal([new MapEditorBrushId(2), new MapEditorBrushId(3)], removed.Ids);
        Assert.Equal(["keep"], workspace.Snapshot.BrushDocument!.Layers.Background.Brushes
            .Select(brush => brush.Name));
        Assert.Equal(1, workspace.Snapshot.Revision);

        workspace.Undo();

        Assert.Equal(["keep", "erase first", "erase second"],
            workspace.Snapshot.BrushDocument.Layers.Background.Brushes
            .Select(brush => brush.Name));
    }

    [Fact]
    public void BrushChangesTrackOrderDirtyLayersAndUndoRedo()
    {
        MapEditorBrushDocument document = Document(Layers());
        FakeStore store = Open(out MapEditorWorkspace workspace, document);

        MapEditorBrushAdded first = Assert.IsType<MapEditorBrushAdded>(
            workspace.AddBrush(Draft("first")).Update?.Change);
        MapEditorBrushAdded second = Assert.IsType<MapEditorBrushAdded>(
            workspace.AddBrush(Draft("second")).Update?.Change);
        Assert.True(workspace.Snapshot.BrushDocument!.Layers.Background.BakeDirty);

        workspace.ReorderBrush(second.Id, 0);
        Assert.Equal([second.Id, first.Id], workspace.Snapshot.BrushDocument.Layers.Background
            .Brushes.Select(brush => brush.Id));
        workspace.MoveBrushToLayer(first.Id, MapEditorLayer.SOLID);
        Assert.True(workspace.Snapshot.BrushDocument.Layers.Solid.BakeDirty);

        workspace.Undo();
        Assert.Equal([second.Id, first.Id], workspace.Snapshot.BrushDocument.Layers.Background
            .Brushes.Select(brush => brush.Id));
        Assert.Empty(workspace.Snapshot.BrushDocument.Layers.Solid.Brushes);
        workspace.Redo();
        Assert.Equal(first.Id,
            Assert.Single(workspace.Snapshot.BrushDocument.Layers.Solid.Brushes).Id);
        Assert.Null(store.SavedDocument);
    }

    [Fact]
    public void RemoveAndReorderUndoRedoRestoreExactBrushesAndOrder()
    {
        MapEditorBrush first = Brush(1, "first", MapEditorLayer.BACKGROUND);
        MapEditorBrush second = Brush(2, "second", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace workspace, Document(Layers(), first, second));

        workspace.RemoveBrush(first.Id);
        Assert.Equal([second.Id], BackgroundIds());
        workspace.Undo();
        Assert.Equal([first.Id, second.Id], BackgroundIds());
        workspace.Redo();
        Assert.Equal([second.Id], BackgroundIds());

        workspace.Undo();
        workspace.ReorderBrush(second.Id, 0);
        Assert.Equal([second.Id, first.Id], BackgroundIds());
        workspace.Undo();
        Assert.Equal([first.Id, second.Id], BackgroundIds());
        workspace.Redo();
        Assert.Equal([second.Id, first.Id], BackgroundIds());
        return;

        MapEditorBrushId[] BackgroundIds() => workspace.Snapshot.BrushDocument!.Layers.Background
            .Brushes.Select(brush => brush.Id).ToArray();
    }

    [Fact]
    public void BrushIdsAndOrderSurviveSaveAndReloadAfterLayersAreBaked()
    {
        FakeStore store = Open(out MapEditorWorkspace workspace, Document(Layers()));
        MapEditorBrushId first = Assert.IsType<MapEditorBrushAdded>(
            workspace.AddBrush(Draft("first") with { Visible = false }).Update?.Change).Id;
        MapEditorBrushId second = Assert.IsType<MapEditorBrushAdded>(
            workspace.AddBrush(Draft("second") with { Visible = false }).Update?.Change).Id;
        workspace.ReorderBrush(second, 0);

        Assert.True(workspace.Save().Succeeded);
        MapEditorBrushDocument saved = Assert.IsType<MapEditorBrushDocument>(store.SavedDocument);
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(Definition(), store.SavedManifest!, store.SavedLayers!, saved)));
        Assert.True(workspace.Reload().Succeeded);

        Assert.Equal([second, first], workspace.Snapshot.BrushDocument!.Layers.Background.Brushes
            .Select(brush => brush.Id));
        Assert.Equal(3, workspace.Snapshot.BrushDocument.NextBrushId);
        Assert.False(workspace.Snapshot.CanUndo);
        Assert.False(workspace.Snapshot.CanRedo);
    }

    [Fact]
    public void DuplicateGetsStableIdImmediatelyAboveSourceAndIsOneHistoryEntry()
    {
        MapEditorBrush first = Brush(1, "first", MapEditorLayer.BACKGROUND);
        MapEditorBrush top = Brush(2, "top", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace workspace, Document(Layers(), first, top));

        MapEditorBrushAdded change = Assert.IsType<MapEditorBrushAdded>(
            workspace.DuplicateBrush(first.Id, 8).Update?.Change);

        Assert.Equal(3, change.Id.Value);
        Assert.Equal([first.Id, change.Id, top.Id], workspace.Snapshot.BrushDocument!.Layers
            .Background.Brushes.Select(brush => brush.Id));
        Assert.Equal("first copy", workspace.Snapshot.BrushDocument.Layers.Background.Brushes[1].Name);
        MapEditorBrush duplicate = workspace.Snapshot.BrushDocument.Layers.Background.Brushes[1];
        Assert.Equal(new MapEditorRectBrushShape(9, 10, 3, 4, 0), duplicate.Shape);
        Assert.Equal(new MapEditorPoint(9, 10), duplicate.Projection.Origin);
        Assert.Equal(4, workspace.Snapshot.BrushDocument.NextBrushId);

        workspace.Undo();

        Assert.Equal([first.Id, top.Id], workspace.Snapshot.BrushDocument.Layers.Background.Brushes
            .Select(brush => brush.Id));
    }

    [Fact]
    public void InvalidSelfIntersectingPolygonDoesNotMutateWorkspace()
    {
        Open(out MapEditorWorkspace workspace, Document(Layers()));
        long revision = workspace.Snapshot.Revision;
        MapEditorBrushDraft invalid = Draft("bow tie") with
        {
            Shape = new MapEditorPolygonBrushShape([
                new MapEditorPoint(0, 0), new MapEditorPoint(5, 5),
                new MapEditorPoint(0, 5), new MapEditorPoint(5, 0),
            ]),
        };

        MapEditorOperationResult result = workspace.AddBrush(invalid);

        MapEditorContentFailure failure = Assert.IsType<MapEditorContentFailure>(result.Failure);
        Assert.Contains(failure.Diagnostics,
            diagnostic => diagnostic.Message.Contains("self-intersect", StringComparison.Ordinal));
        Assert.Equal(revision, workspace.Snapshot.Revision);
        Assert.Empty(workspace.Snapshot.BrushDocument!.Layers.Background.Brushes);
    }

    [Fact]
    public void NameOnlyReplacementReusesCleanBakeWhilePixelFieldsDirtyIt()
    {
        MapEditorBrush original = Brush(1, "original", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace metadata, Document(Layers(), original));
        MapEditorBrushDraft renamed = DraftFrom(original) with { Name = "renamed" };

        Assert.True(metadata.ReplaceBrush(original.Id, renamed).Succeeded);
        Assert.False(metadata.Snapshot.BrushDocument!.Layers.Background.BakeDirty);

        AssertPixelEditDirty(draft => draft with
        {
            Shape = new MapEditorRectBrushShape(2, 2, 3, 4, 0)
        });
        AssertPixelEditDirty(draft => draft with
        {
            Material = new MapEditorTextureMaterial(MapEditorTextureReference.Project("other.png"))
        });
        AssertPixelEditDirty(draft => draft with
        {
            Projection = draft.Projection with { ScaleX = 2 }
        });
        AssertPixelEditDirty(draft => draft with { Visible = false });

        void AssertPixelEditDirty(Func<MapEditorBrushDraft, MapEditorBrushDraft> change)
        {
            Open(out MapEditorWorkspace workspace, Document(Layers(), original));
            Assert.True(workspace.ReplaceBrush(original.Id,
                change(DraftFrom(original))).Succeeded);
            Assert.True(workspace.Snapshot.BrushDocument!.Layers.Background.BakeDirty);
        }
    }

    [Fact]
    public void ReorderAndLayerMoveDirtyOnlyTheirAffectedLayers()
    {
        MapEditorBrush first = Brush(1, "first", MapEditorLayer.BACKGROUND);
        MapEditorBrush second = Brush(2, "second", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace reorder, Document(Layers(), first, second));

        reorder.ReorderBrush(second.Id, 0);

        Assert.True(reorder.Snapshot.BrushDocument!.Layers.Background.BakeDirty);
        Assert.False(reorder.Snapshot.BrushDocument.Layers.Solid.BakeDirty);
        Assert.False(reorder.Snapshot.BrushDocument.Layers.Destructible.BakeDirty);

        Open(out MapEditorWorkspace move, Document(Layers(), first, second));
        move.MoveBrushToLayer(first.Id, MapEditorLayer.SOLID);

        Assert.True(move.Snapshot.BrushDocument!.Layers.Background.BakeDirty);
        Assert.True(move.Snapshot.BrushDocument.Layers.Solid.BakeDirty);
        Assert.False(move.Snapshot.BrushDocument.Layers.Destructible.BakeDirty);
    }

    [Fact]
    public void UndoAndRedoTrackSavedStateIdentity()
    {
        MapEditorBrush original = Brush(1, "original", MapEditorLayer.BACKGROUND);
        Open(out MapEditorWorkspace workspace, Document(Layers(), original));
        workspace.ReplaceBrush(original.Id, DraftFrom(original) with { Name = "saved name" });
        Assert.True(workspace.Save().Succeeded);
        Assert.False(workspace.Snapshot.Dirty);

        workspace.Undo();

        Assert.True(workspace.Snapshot.Dirty);
        Assert.True(workspace.Snapshot.CanSave);
        Assert.Equal("original", Assert.Single(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes).Name);

        workspace.Redo();

        Assert.False(workspace.Snapshot.Dirty);
        Assert.False(workspace.Snapshot.CanSave);
        Assert.Equal("saved name", Assert.Single(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes).Name);
    }

    private static FakeStore Open(out MapEditorWorkspace workspace,
        MapEditorBrushDocument? document = null)
    {
        FakeStore store = new();
        MapEditorLayers layers = document?.Layers is { } sources
            ? new MapEditorLayers(sources.Background.Baked, sources.Solid.Baked,
                sources.Destructible.Baked)
            : Layers();
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(Definition(), Manifest(), layers, document)));
        workspace = Assert.IsType<MapEditorWorkspace>(
            MapEditorWorkspace.Open(Definition(), store).Workspace);
        return store;
    }

    private static MapEditorBrushDocument Document(MapEditorLayers layers,
        params MapEditorBrush[] background)
    {
        long next = background.Length == 0 ? 1 : background.Max(brush => brush.Id.Value) + 1;
        return new MapEditorBrushDocument(MapEditorBrushDocument.CURRENT_VERSION, next,
            new MapEditorLayerSources(
                new MapEditorLayerSource(background.ToImmutableArray(), layers.Background, false),
                new MapEditorLayerSource([], layers.Solid, false),
                new MapEditorLayerSource([], layers.Destructible, false)),
            new MapEditorMapOrigin(1, 2));
    }

    private static MapEditorBrush Brush(long id, string name, MapEditorLayer layer) => new(
        new MapEditorBrushId(id), name, layer, new MapEditorRectBrushShape(1, 2, 3, 4, 0),
        new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(1, 2), 1, 1, 0), true);

    private static MapEditorBrushDraft Draft(string name) => new(name, MapEditorLayer.BACKGROUND,
        new MapEditorRectBrushShape(1, 2, 3, 4, 0),
        new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(1, 2), 1, 1, 0));

    private static MapEditorBrushDraft DraftFrom(MapEditorBrush brush) => new(brush.Name,
        brush.Layer, brush.Shape, brush.Material, brush.Projection, brush.Visible);

    private static MapEditorLayers Layers() => new(
        new MapEditorLayerAsset([1], 3, 4),
        new MapEditorLayerAsset([2], 3, 4),
        new MapEditorLayerAsset([3], 3, 4));

    private static MapManifest Manifest() => new() { Name = "Map", SuggestedPlayers = 1 };

    private static ContentDefinition<MapManifest> Definition() => new("test", Manifest(),
        "/pack/maps/test", "/pack/maps/test/map.toml",
        new ContentPackDefinition(new ContentPackManifest("org.test", "Test", "1"), "/pack"));

    private sealed class FakeStore : IMapEditorStore
    {
        public Queue<MapEditorStoreResult<MapEditorStoredMap>> Loads { get; } = new();
        public MapEditorBrushDocument? SavedDocument { get; private set; }
        public MapEditorLayers? SavedLayers { get; private set; }
        public MapManifest? SavedManifest { get; private set; }

        public MapEditorStoreResult<MapEditorStoredMap> Load(
            ContentDefinition<MapManifest> definition) => Loads.Dequeue();

        public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
            ContentDefinition<MapManifest> definition, MapManifest manifest,
            MapEditorLayers layers, int width, int height,
            MapEditorBrushDocument? brushDocument)
        {
            SavedManifest = manifest;
            SavedLayers = layers;
            SavedDocument = brushDocument;
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(
                definition with { Manifest = manifest });
        }
    }
}
