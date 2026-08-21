using System.Collections.Immutable;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorBrushBakingTests
{
    [Fact]
    public void SaveBakesOnlyDirtyLayersAndReusesCleanMissingTextureBake()
    {
        MapEditorLayers baked = Layers();
        MapEditorBrush dirty = Brush(1, "resolved", MapEditorLayer.BACKGROUND,
            "res://resolved.png", visible: true);
        MapEditorBrush cleanMissing = Brush(2, "private", MapEditorLayer.SOLID,
            "res://private.png", visible: true);
        MapEditorBrushDocument document = Document(baked, true, false, false,
            [dirty], [cleanMissing], []);
        FakeResolver resolver = new(new MapEditorTextureData(1, 1, [9, 8, 7, 255]));
        FakeStore store = Open(document, resolver, out MapEditorWorkspace workspace);
        byte[] originalSolid = workspace.Snapshot.Layers.Solid.Png.ToArray();

        MapEditorOperationResult result = workspace.Save();

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.SaveCount);
        Assert.False(store.SavedDocument!.Layers.Background.BakeDirty);
        Assert.Equal(originalSolid, store.SavedLayers!.Solid.Png.ToArray());
        Assert.Equal([MapEditorLayer.BACKGROUND], resolver.ResolvedLayers);
        Assert.Contains(workspace.Snapshot.Diagnostics, diagnostic =>
            diagnostic.Severity == ContentDiagnosticSeverity.WARNING &&
            diagnostic.Message.Contains("Brush 2 'private'", StringComparison.Ordinal));
    }

    [Fact]
    public void DirtyUnresolvedVisibleBrushFailsBeforeCompositionStoreOrWorkspaceMutation()
    {
        MapEditorLayers baked = Layers();
        MapEditorBrush missing = Brush(7, "paid rock", MapEditorLayer.DESTRUCTIBLE,
            "res://paid/rock.png", visible: true);
        MapEditorBrushDocument document = Document(baked, false, false, true,
            [], [], [missing]);
        FakeResolver resolver = new(null);
        FakeStore store = Open(document, resolver, out MapEditorWorkspace workspace);
        byte[] before = workspace.Snapshot.Layers.Destructible.Png.ToArray();
        CountingCompositor compositor = new();
        workspace = Open(document, resolver, store, compositor);
        workspace.AddSpawn(new MapSpawnPoint(1, 1));

        Assert.False(workspace.Snapshot.CanSave);

        MapEditorOperationResult result = workspace.Save();

        MapEditorUnresolvedBrushesFailure failure =
            Assert.IsType<MapEditorUnresolvedBrushesFailure>(result.Failure);
        MapEditorUnresolvedBrush unresolved = Assert.Single(failure.Brushes);
        Assert.Equal(new MapEditorBrushId(7), unresolved.Id);
        Assert.Equal("paid rock", unresolved.Name);
        Assert.Equal("res://paid/rock.png", unresolved.Reference.Location);
        Assert.Equal(0, compositor.CallCount);
        Assert.Equal(0, store.SaveCount);
        Assert.True(workspace.Snapshot.BrushDocument!.Layers.Destructible.BakeDirty);
        Assert.Equal(before, workspace.Snapshot.Layers.Destructible.Png.ToArray());
    }

    [Fact]
    public void HiddenUnresolvedBrushDoesNotBlockBake()
    {
        MapEditorLayers baked = Layers();
        MapEditorBrush hidden = Brush(1, "hidden", MapEditorLayer.SOLID,
            "res://missing.png", visible: false);
        MapEditorBrushDocument document = Document(baked, false, true, false,
            [], [hidden], []);
        FakeResolver resolver = new(null);
        FakeStore store = Open(document, resolver, out MapEditorWorkspace workspace);
        workspace.AddSpawn(new MapSpawnPoint(1, 1));
        Assert.True(workspace.Snapshot.CanSave);

        Assert.True(workspace.Save().Succeeded);

        Assert.Equal(1, store.SaveCount);
        Assert.False(store.SavedDocument!.Layers.Solid.BakeDirty);
    }

    [Fact]
    public void ResolverCacheIsInvalidatedOnReload()
    {
        MapEditorLayers baked = Layers();
        MapEditorBrush brush = Brush(1, "missing", MapEditorLayer.BACKGROUND,
            "res://missing.png", visible: true);
        MapEditorBrushDocument document = Document(baked, false, false, false,
            [brush], [], []);
        FakeResolver resolver = new(null);
        FakeStore store = Open(document, resolver, out MapEditorWorkspace workspace);
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Success(Stored(document)));

        Assert.Equal(1, resolver.ResolveCount);
        Assert.True(workspace.Reload().Succeeded);

        Assert.Equal(1, resolver.InvalidateCount);
        Assert.Equal(2, resolver.ResolveCount);
    }

    private static FakeStore Open(MapEditorBrushDocument document, FakeResolver resolver,
        out MapEditorWorkspace workspace)
    {
        FakeStore store = new();
        workspace = Open(document, resolver, store,
            new MapEditorLayerCompositor(resolver));
        return store;
    }

    private static MapEditorWorkspace Open(MapEditorBrushDocument document,
        FakeResolver resolver, FakeStore store, IMapEditorLayerCompositor compositor)
    {
        store.Loads.Enqueue(MapEditorStoreResult<MapEditorStoredMap>.Success(Stored(document)));
        return Assert.IsType<MapEditorWorkspace>(MapEditorWorkspace.Open(
            Definition(), store, resolver, compositor).Workspace);
    }

    private static MapEditorStoredMap Stored(MapEditorBrushDocument document) => new(
        Definition(), Manifest(), new MapEditorLayers(document.Layers.Background.Baked,
            document.Layers.Solid.Baked, document.Layers.Destructible.Baked), document);

    private static MapEditorBrushDocument Document(MapEditorLayers baked,
        bool backgroundDirty, bool solidDirty, bool destructibleDirty,
        ImmutableArray<MapEditorBrush> background,
        ImmutableArray<MapEditorBrush> solid,
        ImmutableArray<MapEditorBrush> destructible) => new(
        MapEditorBrushDocument.CURRENT_VERSION, 20,
        new MapEditorLayerSources(
            new MapEditorLayerSource(background, baked.Background, backgroundDirty),
            new MapEditorLayerSource(solid, baked.Solid, solidDirty),
            new MapEditorLayerSource(destructible, baked.Destructible, destructibleDirty)));

    private static MapEditorBrush Brush(long id, string name, MapEditorLayer layer,
        string path, bool visible) => new(new MapEditorBrushId(id), name, layer,
        new MapEditorRectBrushShape(0, 0, 4, 3, 0),
        new MapEditorTextureMaterial(MapEditorTextureReference.Project(path)),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(0, 0), 1, 1, 0), visible);

    private static MapEditorLayers Layers() => new(
        new MapEditorLayerAsset([1, 2], 4, 3),
        new MapEditorLayerAsset([3, 4], 4, 3),
        new MapEditorLayerAsset([5, 6], 4, 3));

    private static MapManifest Manifest() => new() { Name = "Map", SuggestedPlayers = 1 };

    private static ContentDefinition<MapManifest> Definition() => new("test", Manifest(),
        "/pack/maps/test", "/pack/maps/test/map.toml",
        new ContentPackDefinition(new ContentPackManifest("org.test", "Test", "1"), "/pack"));

    private sealed class FakeResolver(MapEditorTextureData? resolved) : IMapEditorTextureResolver
    {
        private readonly HashSet<MapEditorLayer> _resolvedLayers = [];
        public int ResolveCount { get; private set; }
        public int InvalidateCount { get; private set; }
        public IReadOnlyCollection<MapEditorLayer> ResolvedLayers => _resolvedLayers;

        public MapEditorTextureResolution Resolve(MapEditorTextureReference reference)
        {
            ResolveCount++;
            if (resolved != null && reference.Location == "res://resolved.png")
            {
                _resolvedLayers.Add(MapEditorLayer.BACKGROUND);
                return new MapEditorTextureResolution(MapEditorTextureResolutionStatus.RESOLVED,
                    reference, resolved, "resolved", reference.Location);
            }

            return new MapEditorTextureResolution(MapEditorTextureResolutionStatus.MISSING,
                reference, null, "missing");
        }

        public void Invalidate() => InvalidateCount++;
    }

    private sealed class CountingCompositor : IMapEditorLayerCompositor
    {
        public int CallCount { get; private set; }

        public MapEditorLayerCompositionResult Compose(MapEditorLayerSource layer,
            int width, int height)
        {
            CallCount++;
            return new MapEditorLayerCompositionResult(
                new MapEditorLayerAsset([9], width, height), [], null);
        }
    }

    private sealed class FakeStore : IMapEditorStore
    {
        public Queue<MapEditorStoreResult<MapEditorStoredMap>> Loads { get; } = new();
        public int SaveCount { get; private set; }
        public MapEditorLayers? SavedLayers { get; private set; }
        public MapEditorBrushDocument? SavedDocument { get; private set; }

        public MapEditorStoreResult<MapEditorStoredMap> Load(
            ContentDefinition<MapManifest> definition) => Loads.Dequeue();

        public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
            ContentDefinition<MapManifest> definition, MapManifest manifest,
            MapEditorLayers layers, int width, int height,
            MapEditorBrushDocument? brushDocument)
        {
            SaveCount++;
            SavedLayers = layers;
            SavedDocument = brushDocument;
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(definition);
        }
    }
}
