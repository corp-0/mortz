using System.Collections.Immutable;
using Mortz.Content;
using Mortz.Core.Sim;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorWorkspace
{
    private readonly IMapEditorStore? _store;
    private readonly IMapEditorTextureResolver _textureResolver;
    private readonly IMapEditorLayerCompositor _compositor;
    private readonly Stack<HistoryEntry> _undo = [];
    private readonly Stack<HistoryEntry> _redo = [];
    private ContentDefinition<MapManifest>? _definition;
    private string _mapId;
    private string _validationSource;
    private MapEditorLayers _layers;
    private MapEditorBrushDocument? _brushDocument;
    private int _width;
    private int _height;
    private long _originX;
    private long _originY;
    private long _boundsWidth;
    private long _boundsHeight;
    private string _name;
    private int _suggestedPlayers;
    private ImmutableArray<MapEditorZone> _zones;
    private ImmutableArray<MapEditorSpawn> _spawnPoints;
    private long _nextZoneId = 1;
    private long _nextSpawnId = 1;
    private long _revision;
    private long _savedRevision;
    private long _stateId;
    private long _savedStateId;
    private long _nextStateId = 1;

    private sealed record EditableState(
        string Name,
        int SuggestedPlayers,
        ImmutableArray<MapEditorZone> Zones,
        ImmutableArray<MapEditorSpawn> SpawnPoints,
        MapEditorLayers Layers,
        MapEditorBrushDocument? BrushDocument,
        int Width,
        int Height,
        long OriginX,
        long OriginY,
        long BoundsWidth,
        long BoundsHeight,
        long NextZoneId,
        long NextSpawnId,
        long StateId);

    private sealed record HistoryEntry(EditableState State, MapEditorChange Change);

    public MapEditorWorkspace(string mapId, MapManifest manifest, MapEditorLayers layers,
        IMapEditorTextureResolver? textureResolver = null,
        IMapEditorLayerCompositor? compositor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        _mapId = mapId;
        _textureResolver = textureResolver ?? new MapEditorTextureResolver(
            new UnavailableMapEditorTextureAccess());
        _compositor = compositor ?? new MapEditorLayerCompositor(_textureResolver);
        _validationSource = mapId;
        _layers = layers;
        _name = manifest.Name;
        _zones = [];
        _spawnPoints = [];
        Adopt(Prepare(manifest, layers, null, 1, 1, 0), 0);
        Snapshot = BuildSnapshot();
    }

    private MapEditorWorkspace(IMapEditorStore store, MapEditorStoredMap stored,
        IMapEditorTextureResolver textureResolver, IMapEditorLayerCompositor compositor)
    {
        _store = store;
        _textureResolver = textureResolver;
        _compositor = compositor;
        _definition = stored.Definition;
        _mapId = stored.Definition.Id;
        _validationSource = stored.Definition.ManifestPath;
        _layers = stored.Layers;
        _name = stored.Manifest.Name;
        _zones = [];
        _spawnPoints = [];
        Adopt(Prepare(stored.Manifest, stored.Layers, stored.BrushDocument, 1, 1, 0), 0);
        Snapshot = BuildSnapshot();
    }

    public MapEditorSnapshot Snapshot { get; private set; }

    public static MapEditorOpenResult Open(ContentDefinition<MapManifest> definition,
        IMapEditorStore store,
        IMapEditorTextureResolver? textureResolver = null,
        IMapEditorLayerCompositor? compositor = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(store);
        MapEditorStoreResult<MapEditorStoredMap> result = store.Load(definition);
        if (result.Value == null)
            return new MapEditorOpenResult(null, null, RequiredFailure(result.Failure));
        try
        {
            IMapEditorTextureResolver resolver = textureResolver ?? new MapEditorTextureResolver(
                new UnavailableMapEditorTextureAccess());
            MapEditorWorkspace workspace = new(store, result.Value, resolver,
                compositor ?? new MapEditorLayerCompositor(resolver));
            return new MapEditorOpenResult(workspace,
                new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()), null);
        }
        catch (ArgumentException exception)
        {
            return new MapEditorOpenResult(null, null,
                ContentFailure(definition.DirectoryPath, exception.Message));
        }
    }

    public MapEditorUpdate AddZone(MapEditorZoneDraft zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        EditableState before = Capture();
        MapEditorZoneId id = AllocateZoneId();
        _zones = _zones.Add(CreateZone(id, zone));
        return Commit(new MapEditorZoneAdded(id), before, refitBounds: true);
    }

    public MapEditorUpdate? ReplaceZone(MapEditorZoneId id, MapEditorZoneDraft zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        int index = FindZone(id);
        if (index < 0)
            return null;
        MapEditorZone replacement = CreateZone(id, zone);
        if (ZoneValuesEqual(_zones[index], replacement))
            return null;
        bool boundsChanged = _zones[index].Shape != replacement.Shape;
        EditableState before = Capture();
        _zones = _zones.SetItem(index, replacement);
        return Commit(new MapEditorZoneReplaced(id), before,
            refitBounds: boundsChanged);
    }

    public MapEditorUpdate? RemoveZone(MapEditorZoneId id)
    {
        int index = FindZone(id);
        if (index < 0)
            return null;
        EditableState before = Capture();
        _zones = _zones.RemoveAt(index);
        return Commit(new MapEditorZoneRemoved(id), before, refitBounds: true);
    }

    public MapEditorOperationResult DuplicateZone(MapEditorZoneId id, int offset)
    {
        int index = FindZone(id);
        if (index < 0)
            return new MapEditorOperationResult(null, null);
        if (_nextZoneId == long.MaxValue)
            return IdentityOverflow("zone");
        try
        {
            MapEditorZone source = _zones[index];
            MapZoneDef moved = MapEditorGeometry.Move(new MapZoneDef
            {
                Name = source.Name,
                Tags = source.Tags.ToArray(),
                Shape = source.Shape,
                Effects = source.Effects.ToArray(),
            }, new Vec2(offset, offset));
            MapEditorZoneId duplicateId = new(_nextZoneId);
            MapEditorZone duplicate = CreateZone(duplicateId, new MapEditorZoneDraft(
                UniqueZoneDuplicateName(source.Name), source.Tags, moved.Shape, source.Effects));
            EditableState before = Capture();
            _nextZoneId++;
            _zones = _zones.Insert(index + 1, duplicate);
            return MapEditorOperationResult.Success(Commit(new MapEditorZoneAdded(duplicateId),
                before, refitBounds: true));
        }
        catch (OverflowException)
        {
            return IdentityOverflow("zone");
        }
    }

    public MapEditorUpdate AddSpawn(MapSpawnPoint spawn)
    {
        EditableState before = Capture();
        MapEditorSpawnId id = AllocateSpawnId();
        _spawnPoints = _spawnPoints.Add(new MapEditorSpawn(id, spawn));
        return Commit(new MapEditorSpawnAdded(id), before, refitBounds: true);
    }

    public MapEditorUpdate? ReplaceSpawn(MapEditorSpawnId id, MapSpawnPoint spawn)
    {
        int index = FindSpawn(id);
        if (index < 0 || _spawnPoints[index].Value == spawn)
            return null;
        MapSpawnPoint previous = _spawnPoints[index].Value;
        EditableState before = Capture();
        _spawnPoints = _spawnPoints.SetItem(index, new MapEditorSpawn(id, spawn));
        return Commit(new MapEditorSpawnReplaced(id), before,
            refitBounds: previous.X != spawn.X || previous.Y != spawn.Y);
    }

    public MapEditorUpdate? RemoveSpawn(MapEditorSpawnId id)
    {
        int index = FindSpawn(id);
        if (index < 0)
            return null;
        EditableState before = Capture();
        _spawnPoints = _spawnPoints.RemoveAt(index);
        return Commit(new MapEditorSpawnRemoved(id), before, refitBounds: true);
    }

    public MapEditorOperationResult DuplicateSpawn(MapEditorSpawnId id, int offset)
    {
        int index = FindSpawn(id);
        if (index < 0)
            return new MapEditorOperationResult(null, null);
        if (_nextSpawnId == long.MaxValue)
            return IdentityOverflow("spawn");
        try
        {
            MapEditorSpawn source = _spawnPoints[index];
            MapSpawnPoint moved = source.Value with
            {
                X = checked(source.Value.X + offset),
                Y = checked(source.Value.Y + offset),
            };
            MapEditorSpawnId duplicateId = new(_nextSpawnId);
            EditableState before = Capture();
            _nextSpawnId++;
            _spawnPoints = _spawnPoints.Insert(index + 1, new MapEditorSpawn(duplicateId, moved));
            return MapEditorOperationResult.Success(Commit(new MapEditorSpawnAdded(duplicateId),
                before, refitBounds: true));
        }
        catch (OverflowException)
        {
            return IdentityOverflow("spawn");
        }
    }

    public MapEditorOperationResult InitializeBrushSource()
    {
        if (_brushDocument != null)
            return new MapEditorOperationResult(null, null);
        EditableState before = Capture();
        _brushDocument = MapEditorBrushDocument.Empty(_layers, bakeDirty: true);
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushSourceInitialized(), before,
            refitBounds: true));
    }

    public MapEditorOperationResult CancelBrushSourceInitialization()
    {
        if (_undo.TryPeek(out HistoryEntry? entry) &&
            entry.Change is MapEditorBrushSourceInitialized && _brushDocument != null)
            return Undo();
        return new MapEditorOperationResult(null, null);
    }

    public MapEditorOperationResult AddBrush(MapEditorBrushDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        MapEditorBrush brush = new(new MapEditorBrushId(_brushDocument.NextBrushId), draft.Name,
            draft.Layer, draft.Shape, draft.Material, draft.Projection, draft.Visible);
        MapEditorLayerSource layer = _brushDocument.Layers.Get(draft.Layer);
        MapEditorBrushDocument candidate = _brushDocument with
        {
            NextBrushId = checked(_brushDocument.NextBrushId + 1),
            Layers = _brushDocument.Layers.Set(draft.Layer,
                layer with { Brushes = layer.Brushes.Add(brush), BakeDirty = true }),
        };
        if (ValidateCandidate(candidate) is { } failure)
            return MapEditorOperationResult.Failed(failure);
        EditableState before = Capture();
        _brushDocument = candidate;
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushAdded(brush.Id), before,
            refitBounds: true));
    }

    public MapEditorOperationResult AddBrushes(IReadOnlyList<MapEditorBrushDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (drafts.Count == 0)
            return new MapEditorOperationResult(null, null);
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (_brushDocument.NextBrushId > long.MaxValue - drafts.Count)
            return IdentityOverflow("brush");
        MapEditorBrushDocument candidate = _brushDocument;
        ImmutableArray<MapEditorBrushId>.Builder ids =
            ImmutableArray.CreateBuilder<MapEditorBrushId>(drafts.Count);
        foreach (MapEditorBrushDraft draft in drafts)
        {
            ArgumentNullException.ThrowIfNull(draft);
            MapEditorBrushId id = new(candidate.NextBrushId);
            MapEditorBrush brush = new(id, draft.Name, draft.Layer, draft.Shape,
                draft.Material, draft.Projection, draft.Visible);
            MapEditorLayerSource layer = candidate.Layers.Get(draft.Layer);
            candidate = candidate with
            {
                NextBrushId = checked(candidate.NextBrushId + 1),
                Layers = candidate.Layers.Set(draft.Layer,
                    layer with { Brushes = layer.Brushes.Add(brush), BakeDirty = true }),
            };
            ids.Add(id);
        }
        if (ValidateCandidate(candidate) is { } failure)
            return MapEditorOperationResult.Failed(failure);
        EditableState before = Capture();
        _brushDocument = candidate;
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushesAdded(ids.MoveToImmutable()),
            before, refitBounds: true));
    }

    public MapEditorOperationResult SaveStamp(MapEditorBrushId brushId)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(brushId, out MapEditorLayer layer, out int index))
            return new MapEditorOperationResult(null, null);
        if (_brushDocument.NextStampId == long.MaxValue)
            return IdentityOverflow("stamp");
        try
        {
            ImmutableArray<MapEditorStamp> stamps = _brushDocument.Stamps.IsDefault
                ? [] : _brushDocument.Stamps;
            MapEditorBrush brush = _brushDocument.Layers.Get(layer).Brushes[index];
            MapEditorStampId id = new(_brushDocument.NextStampId);
            MapEditorStamp stamp = new(id, UniqueStampName(stamps, brush.Name),
                MapEditorStampGeometry.CreateTemplate(brush));
            MapEditorBrushDocument candidate = _brushDocument with
            {
                NextStampId = checked(_brushDocument.NextStampId + 1),
                Stamps = stamps.Add(stamp),
            };
            if (ValidateCandidate(candidate) is { } failure)
                return MapEditorOperationResult.Failed(failure);
            EditableState before = Capture();
            _brushDocument = candidate;
            return MapEditorOperationResult.Success(Commit(new MapEditorStampSaved(id), before));
        }
        catch (OverflowException)
        {
            return IdentityOverflow("stamp");
        }
    }

    public MapEditorOperationResult RemoveStamp(MapEditorStampId id)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        ImmutableArray<MapEditorStamp> stamps = _brushDocument.Stamps.IsDefault
            ? [] : _brushDocument.Stamps;
        int index = -1;
        for (int stampIndex = 0; stampIndex < stamps.Length; stampIndex++)
        {
            if (stamps[stampIndex].Id == id)
            {
                index = stampIndex;
                break;
            }
        }
        if (index < 0)
            return new MapEditorOperationResult(null, null);
        EditableState before = Capture();
        _brushDocument = _brushDocument with { Stamps = stamps.RemoveAt(index) };
        return MapEditorOperationResult.Success(Commit(new MapEditorStampRemoved(id), before));
    }

    public MapEditorOperationResult ReplaceBrush(MapEditorBrushId id, MapEditorBrushDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(id, out MapEditorLayer oldLayer, out int index))
            return new MapEditorOperationResult(null, null);
        if (draft.Layer != oldLayer)
            return MoveBrushToLayer(id, draft.Layer, draft);
        MapEditorBrush existing = _brushDocument.Layers.Get(oldLayer).Brushes[index];
        MapEditorBrush replacement = new(id, draft.Name, oldLayer, draft.Shape, draft.Material,
            draft.Projection, draft.Visible);
        if (BrushValuesEqual(existing, replacement))
            return new MapEditorOperationResult(null, null);
        MapEditorLayerSource source = _brushDocument.Layers.Get(oldLayer);
        bool pixelsChanged = !BrushPixelsEqual(existing, replacement);
        MapEditorBrushDocument candidate = _brushDocument with
        {
            Layers = _brushDocument.Layers.Set(oldLayer, source with
            {
                Brushes = source.Brushes.SetItem(index, replacement),
                BakeDirty = source.BakeDirty || pixelsChanged,
            }),
        };
        if (ValidateCandidate(candidate) is { } failure)
            return MapEditorOperationResult.Failed(failure);
        EditableState before = Capture();
        _brushDocument = candidate;
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushReplaced(id), before,
            refitBounds: !ShapesEqual(existing.Shape, replacement.Shape)));
    }

    public MapEditorOperationResult RemoveBrush(MapEditorBrushId id)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(id, out MapEditorLayer layer, out int index))
            return new MapEditorOperationResult(null, null);
        EditableState before = Capture();
        MapEditorLayerSource source = _brushDocument.Layers.Get(layer);
        _brushDocument = _brushDocument with
        {
            Layers = _brushDocument.Layers.Set(layer,
                source with { Brushes = source.Brushes.RemoveAt(index), BakeDirty = true }),
        };
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushRemoved(id), before,
            refitBounds: true));
    }

    public MapEditorOperationResult RemoveBrushes(IReadOnlySet<MapEditorBrushId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return new MapEditorOperationResult(null, null);
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        MapEditorBrushDocument candidate = _brushDocument;
        ImmutableArray<MapEditorBrushId>.Builder removed = ImmutableArray.CreateBuilder<MapEditorBrushId>();
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorLayerSource source = candidate.Layers.Get(layer);
            ImmutableArray<MapEditorBrush> brushes = source.Brushes
                .Where(brush => !ids.Contains(brush.Id)).ToImmutableArray();
            if (brushes.Length == source.Brushes.Length)
                continue;
            removed.AddRange(source.Brushes.Where(brush => ids.Contains(brush.Id))
                .Select(brush => brush.Id));
            candidate = candidate with
            {
                Layers = candidate.Layers.Set(layer,
                    source with { Brushes = brushes, BakeDirty = true }),
            };
        }
        if (removed.Count == 0)
            return new MapEditorOperationResult(null, null);
        if (ValidateCandidate(candidate) is { } failure)
            return MapEditorOperationResult.Failed(failure);
        EditableState before = Capture();
        _brushDocument = candidate;
        return MapEditorOperationResult.Success(Commit(
            new MapEditorBrushesRemoved(removed.ToImmutable()), before, refitBounds: true));
    }

    public MapEditorOperationResult DuplicateBrush(MapEditorBrushId id, int offset = 0)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(id, out MapEditorLayer layer, out int index))
            return new MapEditorOperationResult(null, null);
        MapEditorLayerSource source = _brushDocument.Layers.Get(layer);
        MapEditorBrush original = source.Brushes[index];
        MapEditorBrushId duplicateId = new(_brushDocument.NextBrushId);
        if (_brushDocument.NextBrushId == long.MaxValue)
            return IdentityOverflow("brush");
        MapEditorBrush duplicate;
        try
        {
            duplicate = MapEditorGeometry.Move(original, offset, offset) with
            {
                Id = duplicateId,
                Name = UniqueDuplicateName(source.Brushes, original.Name),
            };
        }
        catch (OverflowException)
        {
            return IdentityOverflow("brush");
        }

        EditableState before = Capture();
        _brushDocument = _brushDocument with
        {
            NextBrushId = checked(_brushDocument.NextBrushId + 1),
            Layers = _brushDocument.Layers.Set(layer, source with
            {
                Brushes = source.Brushes.Insert(index + 1, duplicate),
                BakeDirty = true,
            }),
        };
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushAdded(duplicateId), before,
            refitBounds: true));
    }

    public MapEditorOperationResult ReorderBrush(MapEditorBrushId id, int destinationIndex)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(id, out MapEditorLayer layer, out int index))
            return new MapEditorOperationResult(null, null);
        MapEditorLayerSource source = _brushDocument.Layers.Get(layer);
        if (destinationIndex < 0 || destinationIndex >= source.Brushes.Length)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (index == destinationIndex)
            return new MapEditorOperationResult(null, null);
        EditableState before = Capture();
        MapEditorBrush brush = source.Brushes[index];
        ImmutableArray<MapEditorBrush> reordered = source.Brushes.RemoveAt(index)
            .Insert(destinationIndex, brush);
        _brushDocument = _brushDocument with
        {
            Layers = _brushDocument.Layers.Set(layer,
                source with { Brushes = reordered, BakeDirty = true }),
        };
        return MapEditorOperationResult.Success(Commit(new MapEditorBrushReordered(id), before));
    }

    public MapEditorOperationResult MoveBrushToLayer(MapEditorBrushId id,
        MapEditorLayer destination) => MoveBrushToLayer(id, destination, null);

    private MapEditorOperationResult MoveBrushToLayer(MapEditorBrushId id,
        MapEditorLayer destination, MapEditorBrushDraft? replacement)
    {
        if (_brushDocument == null)
            return BrushEditingUnavailable();
        if (!TryFindBrush(id, out MapEditorLayer sourceLayer, out int index))
            return new MapEditorOperationResult(null, null);
        if (sourceLayer == destination)
            return new MapEditorOperationResult(null, null);
        MapEditorLayerSource source = _brushDocument.Layers.Get(sourceLayer);
        MapEditorLayerSource target = _brushDocument.Layers.Get(destination);
        MapEditorBrush previous = source.Brushes[index];
        MapEditorBrush moved = replacement == null
            ? previous with { Layer = destination }
            : new MapEditorBrush(id, replacement.Name, destination, replacement.Shape,
                replacement.Material, replacement.Projection, replacement.Visible);
        MapEditorLayerSources layers = _brushDocument.Layers
            .Set(sourceLayer, source with
            {
                Brushes = source.Brushes.RemoveAt(index),
                BakeDirty = true,
            })
            .Set(destination, target with
            {
                Brushes = target.Brushes.Add(moved),
                BakeDirty = true,
            });
        MapEditorBrushDocument candidate = _brushDocument with { Layers = layers };
        if (ValidateCandidate(candidate) is { } failure)
            return MapEditorOperationResult.Failed(failure);
        EditableState before = Capture();
        _brushDocument = candidate;
        return MapEditorOperationResult.Success(Commit(
            new MapEditorBrushMovedToLayer(id, sourceLayer, destination), before,
            refitBounds: replacement != null && !ShapesEqual(previous.Shape, moved.Shape)));
    }

    public MapEditorOperationResult Undo()
    {
        if (!_undo.TryPop(out HistoryEntry? entry))
            return new MapEditorOperationResult(null, null);
        EditableState current = Capture();
        Restore(entry.State);
        _redo.Push(new HistoryEntry(current, entry.Change));
        _revision = checked(_revision + 1);
        Snapshot = BuildSnapshot();
        return MapEditorOperationResult.Success(new MapEditorUpdate(Snapshot,
            new MapEditorUndoApplied(entry.Change)));
    }

    public MapEditorOperationResult Redo()
    {
        if (!_redo.TryPop(out HistoryEntry? entry))
            return new MapEditorOperationResult(null, null);
        EditableState current = Capture();
        Restore(entry.State);
        _undo.Push(new HistoryEntry(current, entry.Change));
        _revision = checked(_revision + 1);
        Snapshot = BuildSnapshot();
        return MapEditorOperationResult.Success(new MapEditorUpdate(Snapshot,
            new MapEditorRedoApplied(entry.Change)));
    }

    public MapEditorOperationResult Save()
    {
        if (_store == null || _definition == null)
            return MissingStore();
        MapEditorRuntimeManifestResult runtimeManifest = TryBuildRuntimeManifest();
        if (runtimeManifest.Manifest == null)
            return MapEditorOperationResult.Failed(RequiredFailure(runtimeManifest.Failure));

        MapEditorLayers layersToSave = _layers;
        MapEditorBrushDocument? documentToSave = _brushDocument;
        if (documentToSave != null)
        {
            ImmutableArray<MapEditorLayer> dirtyLayers = Enum.GetValues<MapEditorLayer>()
                .Where(layer => documentToSave.Layers.Get(layer).BakeDirty).ToImmutableArray();
            ImmutableArray<MapEditorUnresolvedBrush> unresolved = FindUnresolved(
                documentToSave, dirtyLayers);
            if (!unresolved.IsEmpty)
                return MapEditorOperationResult.Failed(
                    new MapEditorUnresolvedBrushesFailure(unresolved));

            foreach (MapEditorLayer layer in dirtyLayers)
            {
                MapEditorLayerSource source = documentToSave.Layers.Get(layer);
                MapEditorLayerCompositionResult composition = _compositor.Compose(
                    source, CurrentBounds());
                if (!composition.Unresolved.IsEmpty)
                {
                    return MapEditorOperationResult.Failed(
                        new MapEditorUnresolvedBrushesFailure(composition.Unresolved));
                }

                if (composition.Baked == null)
                {
                    return MapEditorOperationResult.Failed(new MapEditorCompositionFailure(
                        layer, composition.Error ?? "Could not build the layer image."));
                }

                MapEditorLayerAsset baked = composition.Baked;
                layersToSave = SetLayer(layersToSave, layer, baked);
                documentToSave = documentToSave with
                {
                    Layers = documentToSave.Layers.Set(layer,
                        source with { Baked = baked, BakeDirty = false }),
                };
            }
        }

        MapEditorStoreResult<ContentDefinition<MapManifest>> result = _store.Save(
            _definition, runtimeManifest.Manifest, layersToSave, _width, _height, documentToSave);
        if (result.Value == null)
            return MapEditorOperationResult.Failed(RequiredFailure(result.Failure));
        _definition = result.Value;
        _layers = layersToSave;
        _brushDocument = documentToSave;
        _savedRevision = _revision;
        _savedStateId = _stateId;
        Snapshot = BuildSnapshot();
        return MapEditorOperationResult.Success(new MapEditorUpdate(Snapshot, new MapEditorSaved()));
    }

    public MapEditorOperationResult Reload()
    {
        if (_store == null || _definition == null)
            return MissingStore();
        MapEditorStoreResult<MapEditorStoredMap> result = _store.Load(_definition);
        if (result.Value == null)
            return MapEditorOperationResult.Failed(RequiredFailure(result.Failure));
        MapEditorStoredMap stored = result.Value;
        EditableState prepared;
        try
        {
            prepared = Prepare(stored.Manifest, stored.Layers, stored.BrushDocument,
                _nextZoneId, _nextSpawnId, _nextStateId);
        }
        catch (ArgumentException exception)
        {
            return MapEditorOperationResult.Failed(
                ContentFailure(stored.Definition.DirectoryPath, exception.Message));
        }

        long revision = checked(_revision + 1);
        _nextStateId = checked(_nextStateId + 1);
        _definition = stored.Definition;
        _textureResolver.Invalidate();
        _mapId = stored.Definition.Id;
        _validationSource = stored.Definition.ManifestPath;
        Adopt(prepared, revision);
        _undo.Clear();
        _redo.Clear();
        Snapshot = BuildSnapshot();
        return MapEditorOperationResult.Success(new MapEditorUpdate(Snapshot,
            new MapEditorReloaded()));
    }

    public MapManifest BuildManifest() =>
        TryBuildRuntimeManifest().Manifest ?? BuildAuthoringManifest();

    public MapEditorRuntimeManifestResult TryBuildRuntimeManifest() =>
        MapEditorManifestBuilder.BuildRuntime(_name, _suggestedPlayers, _zones, _spawnPoints,
            _brushDocument, _originX, _originY, _boundsWidth, _boundsHeight, _width, _height,
            _validationSource);

    private MapManifest BuildTranslatedManifest() => MapEditorManifestBuilder.BuildTranslated(
        _name, _suggestedPlayers, _zones, _spawnPoints, _originX, _originY);

    private MapManifest BuildAuthoringManifest() => MapEditorManifestBuilder.BuildAuthoring(
        _name, _suggestedPlayers, _zones, _spawnPoints);

    private static EditableState Prepare(MapManifest manifest, MapEditorLayers layers,
        MapEditorBrushDocument? brushDocument, long nextZoneId, long nextSpawnId,
        long stateId)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Zones);
        ArgumentNullException.ThrowIfNull(manifest.SpawnPoints);
        EnsureMatchingDimensions(layers);
        if (brushDocument != null)
        {
            if (brushDocument.Stamps.IsDefault)
                brushDocument = brushDocument with { Stamps = [] };
            ContentDiagnostic? error = MapEditorBrushValidator.Validate(brushDocument,
                    "editor.json", layers.Solid.Width, layers.Solid.Height)
                .FirstOrDefault(diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
            if (error != null)
                throw new ArgumentException(error.Message, nameof(brushDocument));
        }

        long originX = brushDocument?.Origin.X ?? 0;
        long originY = brushDocument?.Origin.Y ?? 0;
        ImmutableArray<MapEditorZone>.Builder zones = ImmutableArray.CreateBuilder<MapEditorZone>();
        foreach (MapZoneDef zone in manifest.Zones)
        {
            ArgumentNullException.ThrowIfNull(zone);
            ArgumentNullException.ThrowIfNull(zone.Tags);
            ArgumentNullException.ThrowIfNull(zone.Effects);
            ArgumentNullException.ThrowIfNull(zone.Shape);
            MapEditorZone authored = MapEditorMapBoundsFitter.Translate(new MapEditorZone(
                    new MapEditorZoneId(checked(nextZoneId++)), zone.Name,
                    zone.Tags.ToImmutableArray(), zone.Shape, zone.Effects.ToImmutableArray()),
                originX, originY);
            zones.Add(authored);
        }

        ImmutableArray<MapEditorSpawn>.Builder spawns = ImmutableArray.CreateBuilder<MapEditorSpawn>();
        foreach (MapSpawnPoint spawn in manifest.SpawnPoints)
        {
            spawns.Add(new MapEditorSpawn(new MapEditorSpawnId(checked(nextSpawnId++)),
                MapEditorMapBoundsFitter.Translate(spawn, originX, originY)));
        }

        ImmutableArray<MapEditorZone> authoredZones = zones.ToImmutable();
        ImmutableArray<MapEditorSpawn> authoredSpawns = spawns.ToImmutable();
        MapEditorMapBounds bounds = new(originX, originY,
            layers.Solid.Width, layers.Solid.Height);
        if (brushDocument != null)
        {
            MapEditorMapBounds fitted = MapEditorMapBoundsFitter.Fit(
                brushDocument, authoredZones, authoredSpawns, bounds);
            if (fitted != bounds)
            {
                MapEditorLayerSources sources = brushDocument.Layers;
                foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
                {
                    MapEditorLayerSource source = sources.Get(layer);
                    sources = sources.Set(layer, source with { BakeDirty = true });
                }

                brushDocument = brushDocument with
                {
                    Origin = new MapEditorMapOrigin(fitted.X, fitted.Y),
                    Layers = sources,
                };
                bounds = fitted;
            }
        }

        return new EditableState(manifest.Name, manifest.SuggestedPlayers,
            authoredZones, authoredSpawns, layers, brushDocument,
            (int)Math.Min(int.MaxValue, bounds.Width),
            (int)Math.Min(int.MaxValue, bounds.Height), bounds.X, bounds.Y,
            bounds.Width, bounds.Height,
            nextZoneId, nextSpawnId, stateId);
    }

    private MapEditorUpdate Commit(MapEditorChange change, EditableState before,
        bool refitBounds = false)
    {
        if (refitBounds)
            UpdateDynamicBounds();
        _undo.Push(new HistoryEntry(before, change));
        _redo.Clear();
        _stateId = checked(_nextStateId++);
        _revision = checked(_revision + 1);
        Snapshot = BuildSnapshot();
        return new MapEditorUpdate(Snapshot, change);
    }

    private MapEditorSnapshot BuildSnapshot()
    {
        ImmutableArray<ContentDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<ContentDiagnostic>();
        if (_brushDocument == null)
        {
            diagnostics.AddRange(MapManifestValidator.Validate(BuildTranslatedManifest(),
                _validationSource, new MapDimensions(_width, _height)));
        }
        else
        {
            diagnostics.AddRange(MapManifestValidator.Validate(BuildAuthoringManifest(),
                _validationSource));
        }

        if (_brushDocument != null)
        {
            diagnostics.AddRange(MapEditorBrushValidator.Validate(_brushDocument,
                Path.Combine(Path.GetDirectoryName(_validationSource) ?? string.Empty, "editor.json"),
                _width, _height));
            foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
            {
                foreach (MapEditorBrush brush in _brushDocument.Layers.Get(layer).Brushes)
                {
                    if (brush.Material is not MapEditorTextureMaterial texture)
                        continue;
                    MapEditorTextureResolution resolution = _textureResolver.Resolve(texture.Reference);
                    if (resolution.IsResolved)
                        continue;
                    bool blocksBake = brush.Visible &&
                                      _brushDocument.Layers.Get(layer).BakeDirty;
                    diagnostics.Add(new ContentDiagnostic(blocksBake
                            ? ContentDiagnosticSeverity.ERROR
                            : ContentDiagnosticSeverity.WARNING,
                        Path.Combine(Path.GetDirectoryName(_validationSource) ?? string.Empty,
                            "editor.json"),
                        $"Brush {brush.Id.Value} '{brush.Name}' texture is " +
                        $"{resolution.Status.ToString().ToLowerInvariant()}: " +
                        $"{MapEditorTextureResolver.Describe(texture.Reference)}. {resolution.Message}"));
                }
            }
        }

        if (_brushDocument != null &&
            (_boundsWidth > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION ||
             _boundsHeight > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR,
                _validationSource,
                $"The map is {_boundsWidth} x {_boundsHeight}. The maximum size is " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION} x " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION}."));
        }

        return new MapEditorSnapshot(_mapId, _name, _suggestedPlayers, _zones, _spawnPoints,
            _layers, _width, _height, _revision, _savedRevision, diagnostics.ToImmutable(),
            _brushDocument == null ? MapEditorRasterSourceStatus.OBSOLETE : MapEditorRasterSourceStatus.BRUSH_SOURCE,
            _brushDocument, _undo.Count > 0, _redo.Count > 0,
            _stateId, _savedStateId, _originX, _originY, CurrentBounds());
    }

    private EditableState Capture() => new(_name, _suggestedPlayers, _zones, _spawnPoints,
        _layers, _brushDocument, _width, _height, _originX, _originY,
        _boundsWidth, _boundsHeight,
        _nextZoneId, _nextSpawnId, _stateId);

    private void Restore(EditableState state)
    {
        _name = state.Name;
        _suggestedPlayers = state.SuggestedPlayers;
        _zones = state.Zones;
        _spawnPoints = state.SpawnPoints;
        _layers = state.Layers;
        _brushDocument = state.BrushDocument;
        _width = state.Width;
        _height = state.Height;
        _originX = state.OriginX;
        _originY = state.OriginY;
        _boundsWidth = state.BoundsWidth;
        _boundsHeight = state.BoundsHeight;
        _nextZoneId = state.NextZoneId;
        _nextSpawnId = state.NextSpawnId;
        _stateId = state.StateId;
    }

    private void Adopt(EditableState state, long revision)
    {
        Restore(state);
        _revision = revision;
        _savedRevision = revision;
        _savedStateId = state.StateId;
    }

    private MapEditorOperationFailure? ValidateCandidate(MapEditorBrushDocument candidate)
    {
        ImmutableArray<ContentDiagnostic> diagnostics = MapEditorBrushValidator.Validate(candidate,
            Path.Combine(Path.GetDirectoryName(_validationSource) ?? string.Empty, "editor.json"),
            _width, _height);
        return diagnostics.Any(diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
            ? new MapEditorContentFailure(diagnostics)
            : null;
    }

    private MapEditorMapBounds CurrentBounds() =>
        new(_originX, _originY, _boundsWidth, _boundsHeight);

    private void UpdateDynamicBounds()
    {
        if (_brushDocument == null)
            return;
        MapEditorMapBounds fitted = MapEditorMapBoundsFitter.Fit(
            _brushDocument, _zones, _spawnPoints, CurrentBounds());
        if (fitted == CurrentBounds())
            return;

        _originX = fitted.X;
        _originY = fitted.Y;
        _boundsWidth = fitted.Width;
        _boundsHeight = fitted.Height;
        _width = (int)Math.Min(int.MaxValue, fitted.Width);
        _height = (int)Math.Min(int.MaxValue, fitted.Height);
        MapEditorLayerSources sources = _brushDocument.Layers;
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorLayerSource source = sources.Get(layer);
            sources = sources.Set(layer, source with { BakeDirty = true });
        }

        _brushDocument = _brushDocument with
        {
            Origin = new MapEditorMapOrigin(fitted.X, fitted.Y),
            Layers = sources,
        };
    }

    private ImmutableArray<MapEditorUnresolvedBrush> FindUnresolved(
        MapEditorBrushDocument document, ImmutableArray<MapEditorLayer> dirtyLayers)
    {
        ImmutableArray<MapEditorUnresolvedBrush>.Builder unresolved =
            ImmutableArray.CreateBuilder<MapEditorUnresolvedBrush>();
        foreach (MapEditorLayer layer in dirtyLayers)
        {
            foreach (MapEditorBrush brush in document.Layers.Get(layer).Brushes)
            {
                if (!brush.Visible)
                    continue;
                if (brush.Material is not MapEditorTextureMaterial texture)
                    continue;
                MapEditorTextureResolution resolution = _textureResolver.Resolve(texture.Reference);
                if (resolution.IsResolved)
                    continue;
                unresolved.Add(new MapEditorUnresolvedBrush(brush.Id, brush.Name, texture.Reference,
                    resolution.Status, resolution.Message));
            }
        }

        return unresolved.ToImmutable();
    }

    private bool TryFindBrush(MapEditorBrushId id, out MapEditorLayer layer, out int index)
    {
        if (_brushDocument != null)
        {
            foreach (MapEditorLayer candidate in Enum.GetValues<MapEditorLayer>())
            {
                ImmutableArray<MapEditorBrush> brushes = _brushDocument.Layers.Get(candidate).Brushes;
                for (int brushIndex = 0; brushIndex < brushes.Length; brushIndex++)
                {
                    if (brushes[brushIndex].Id == id)
                    {
                        layer = candidate;
                        index = brushIndex;
                        return true;
                    }
                }
            }
        }

        layer = default;
        index = -1;
        return false;
    }

    private static bool BrushValuesEqual(MapEditorBrush left, MapEditorBrush right) =>
        left.Id == right.Id && left.Name == right.Name && left.Layer == right.Layer &&
        left.Material == right.Material && left.Projection == right.Projection &&
        left.Visible == right.Visible && ShapesEqual(left.Shape, right.Shape);

    private static bool BrushPixelsEqual(MapEditorBrush left, MapEditorBrush right) =>
        left.Layer == right.Layer && left.Material == right.Material &&
        left.Projection == right.Projection && left.Visible == right.Visible &&
        ShapesEqual(left.Shape, right.Shape);

    private static string UniqueDuplicateName(ImmutableArray<MapEditorBrush> brushes, string name)
    {
        HashSet<string> names = brushes.Select(brush => brush.Name).ToHashSet(StringComparer.Ordinal);
        string baseName = $"{name} copy";
        if (!names.Contains(baseName))
            return baseName;
        for (int number = 2; ; number++)
        {
            string candidate = $"{baseName} {number}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static string UniqueStampName(ImmutableArray<MapEditorStamp> stamps, string name)
    {
        HashSet<string> names = stamps.Select(stamp => stamp.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
            return name;
        for (int number = 2; ; number++)
        {
            string candidate = $"{name} {number}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private string UniqueZoneDuplicateName(string name)
    {
        HashSet<string> names = _zones.Select(zone => zone.Name).ToHashSet(StringComparer.Ordinal);
        string baseName = $"{name} copy";
        if (!names.Contains(baseName))
            return baseName;
        for (int number = 2; ; number++)
        {
            string candidate = $"{baseName} {number}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static MapEditorOperationResult IdentityOverflow(string objectType) =>
        MapEditorOperationResult.Failed(new MapEditorIdentityOverflowFailure(objectType));

    private static bool ShapesEqual(MapEditorBrushShape left, MapEditorBrushShape right) =>
        left is MapEditorPolygonBrushShape leftPolygon &&
        right is MapEditorPolygonBrushShape rightPolygon
            ? leftPolygon.Vertices.SequenceEqual(rightPolygon.Vertices)
            : left == right;

    private static MapEditorLayers SetLayer(MapEditorLayers layers, MapEditorLayer layer,
        MapEditorLayerAsset asset) => layer switch
        {
            MapEditorLayer.BACKGROUND => layers with { Background = asset },
            MapEditorLayer.SOLID => layers with { Solid = asset },
            MapEditorLayer.DESTRUCTIBLE => layers with { Destructible = asset },
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };

    private MapEditorZoneId AllocateZoneId() => new(checked(_nextZoneId++));
    private MapEditorSpawnId AllocateSpawnId() => new(checked(_nextSpawnId++));

    private int FindZone(MapEditorZoneId id)
    {
        for (int i = 0; i < _zones.Length; i++)
        {
            if (_zones[i].Id == id)
                return i;
        }

        return -1;
    }

    private int FindSpawn(MapEditorSpawnId id)
    {
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i].Id == id)
                return i;
        }

        return -1;
    }

    private static MapEditorZone CreateZone(MapEditorZoneId id, MapEditorZoneDraft zone)
    {
        ArgumentNullException.ThrowIfNull(zone.Name);
        ArgumentNullException.ThrowIfNull(zone.Shape);
        if (zone.Tags.IsDefault)
            throw new ArgumentException("Zone tags must be initialized.", nameof(zone));
        if (zone.Effects.IsDefault)
            throw new ArgumentException("Zone effects must be initialized.", nameof(zone));
        return new MapEditorZone(id, zone.Name, zone.Tags, zone.Shape, zone.Effects);
    }

    private static bool ZoneValuesEqual(MapEditorZone left, MapEditorZone right) =>
        left.Name == right.Name && left.Shape == right.Shape &&
        left.Tags.SequenceEqual(right.Tags) && left.Effects.SequenceEqual(right.Effects);

    private static void EnsureMatchingDimensions(MapEditorLayers layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layers.Background);
        ArgumentNullException.ThrowIfNull(layers.Solid);
        ArgumentNullException.ThrowIfNull(layers.Destructible);
        if (layers.Background.Width != layers.Solid.Width ||
            layers.Background.Height != layers.Solid.Height ||
            layers.Destructible.Width != layers.Solid.Width ||
            layers.Destructible.Height != layers.Solid.Height)
        {
            throw new ArgumentException("Map editor layers must have matching dimensions.",
                nameof(layers));
        }
    }

    private static MapEditorContentFailure ContentFailure(string source, string message) =>
        new([new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message)]);

    private static MapEditorOperationFailure RequiredFailure(MapEditorOperationFailure? failure) =>
        failure ?? new MapEditorIoFailure("Map data couldn't be loaded.");

    private static MapEditorOperationResult BrushEditingUnavailable() =>
        MapEditorOperationResult.Failed(new MapEditorBrushEditingUnavailableFailure());

    private static MapEditorOperationResult MissingStore() =>
        MapEditorOperationResult.Failed(new MapEditorIoFailure(
            "This map can't be saved from here."));
}
