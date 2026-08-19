using System.Collections.Immutable;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorWorkspace
{
    private readonly IMapEditorStore? _store;
    private ContentDefinition<MapManifest>? _definition;
    private string _mapId;
    private string _validationSource;
    private MapEditorLayers _layers;
    private int _width;
    private int _height;
    private string _name;
    private int _suggestedPlayers;
    private ImmutableArray<MapEditorZone> _zones;
    private ImmutableArray<MapEditorSpawn> _spawnPoints;
    private long _nextZoneId = 1;
    private long _nextSpawnId = 1;
    private long _revision;
    private long _savedRevision;

    private sealed record PreparedState(
        string Name,
        int SuggestedPlayers,
        ImmutableArray<MapEditorZone> Zones,
        ImmutableArray<MapEditorSpawn> SpawnPoints,
        MapEditorLayers Layers,
        int Width,
        int Height,
        long NextZoneId,
        long NextSpawnId);

    public MapEditorWorkspace(string mapId, MapManifest manifest, MapEditorLayers layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(manifest);
        _mapId = mapId;
        _validationSource = mapId;
        _layers = layers;
        _name = manifest.Name;
        _zones = [];
        _spawnPoints = [];
        Adopt(manifest, layers, 0);
        Snapshot = BuildSnapshot();
    }

    private MapEditorWorkspace(IMapEditorStore store, MapEditorStoredMap stored)
    {
        _store = store;
        _definition = stored.Definition;
        _mapId = stored.Definition.Id;
        _validationSource = stored.Definition.ManifestPath;
        _layers = stored.Layers;
        _name = stored.Manifest.Name;
        _zones = [];
        _spawnPoints = [];
        Adopt(stored.Manifest, stored.Layers, 0);
        Snapshot = BuildSnapshot();
    }

    public MapEditorSnapshot Snapshot { get; private set; }

    public static MapEditorOpenResult Open(ContentDefinition<MapManifest> definition,
        IMapEditorStore store)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(store);
        MapEditorStoreResult<MapEditorStoredMap> result = store.Load(definition);
        if (result.Value == null)
            return new MapEditorOpenResult(null, null, RequiredFailure(result.Failure));

        MapEditorWorkspace workspace = new(store, result.Value);
        return new MapEditorOpenResult(workspace,
            new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()), null);
    }

    public MapEditorUpdate AddZone(MapEditorZoneDraft zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        MapEditorZoneId id = AllocateZoneId();
        _zones = _zones.Add(CreateZone(id, zone));
        return Commit(new MapEditorZoneAdded(id));
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
        _zones = _zones.SetItem(index, replacement);
        return Commit(new MapEditorZoneReplaced(id));
    }

    public MapEditorUpdate? RemoveZone(MapEditorZoneId id)
    {
        int index = FindZone(id);
        if (index < 0)
            return null;
        _zones = _zones.RemoveAt(index);
        return Commit(new MapEditorZoneRemoved(id));
    }

    public MapEditorUpdate AddSpawn(MapSpawnPoint spawn)
    {
        MapEditorSpawnId id = AllocateSpawnId();
        _spawnPoints = _spawnPoints.Add(new MapEditorSpawn(id, spawn));
        return Commit(new MapEditorSpawnAdded(id));
    }

    public MapEditorUpdate? ReplaceSpawn(MapEditorSpawnId id, MapSpawnPoint spawn)
    {
        int index = FindSpawn(id);
        if (index < 0 || _spawnPoints[index].Value == spawn)
            return null;
        _spawnPoints = _spawnPoints.SetItem(index, new MapEditorSpawn(id, spawn));
        return Commit(new MapEditorSpawnReplaced(id));
    }

    public MapEditorUpdate? RemoveSpawn(MapEditorSpawnId id)
    {
        int index = FindSpawn(id);
        if (index < 0)
            return null;
        _spawnPoints = _spawnPoints.RemoveAt(index);
        return Commit(new MapEditorSpawnRemoved(id));
    }

    public MapEditorOperationResult ReplaceLayer(MapEditorLayer layer, string? path)
    {
        if (_store == null)
            return MissingStore();
        MapEditorStoreResult<MapEditorLayerAsset> result = _store.LoadLayer(
            path, _width, _height);
        if (result.Value == null)
            return MapEditorOperationResult.Failed(RequiredFailure(result.Failure));

        MapEditorLayerAsset asset = result.Value;
        if (GetLayer(layer).Png.Span.SequenceEqual(asset.Png.Span))
            return new MapEditorOperationResult(null, null);

        _layers = layer switch
        {
            MapEditorLayer.BACKGROUND => _layers with { Background = asset },
            MapEditorLayer.SOLID => _layers with { Solid = asset },
            MapEditorLayer.DESTRUCTIBLE => _layers with { Destructible = asset },
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
        return MapEditorOperationResult.Success(Commit(new MapEditorLayerReplaced(layer)));
    }

    public MapEditorOperationResult Save()
    {
        if (_store == null || _definition == null)
            return MissingStore();
        MapManifest manifest = BuildManifest();
        MapEditorStoreResult<ContentDefinition<MapManifest>> result = _store.Save(
            _definition, manifest, _layers, _width, _height);
        if (result.Value == null)
            return MapEditorOperationResult.Failed(RequiredFailure(result.Failure));

        _definition = result.Value;
        _savedRevision = _revision;
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
        PreparedState prepared;
        try
        {
            prepared = Prepare(stored.Manifest, stored.Layers, _nextZoneId, _nextSpawnId);
        }
        catch (ArgumentException exception)
        {
            return MapEditorOperationResult.Failed(new MapEditorContentFailure(
                [new ContentDiagnostic(ContentDiagnosticSeverity.ERROR,
                    stored.Definition.DirectoryPath, exception.Message)]));
        }
        long revision = checked(_revision + 1);
        _definition = stored.Definition;
        _mapId = stored.Definition.Id;
        _validationSource = stored.Definition.ManifestPath;
        Adopt(prepared, revision);
        Snapshot = BuildSnapshot();
        return MapEditorOperationResult.Success(new MapEditorUpdate(Snapshot,
            new MapEditorReloaded()));
    }

    public MapManifest BuildManifest() => new()
    {
        Name = _name,
        SuggestedPlayers = _suggestedPlayers,
        Zones = _zones.Select(zone => new MapZoneDef
        {
            Name = zone.Name,
            Tags = [.. zone.Tags],
            Shape = zone.Shape,
            Effects = [.. zone.Effects],
        }).ToArray(),
        SpawnPoints = _spawnPoints.Select(spawn => spawn.Value).ToArray(),
    };

    private void Adopt(MapManifest manifest, MapEditorLayers layers, long revision)
    {
        PreparedState prepared = Prepare(manifest, layers, _nextZoneId, _nextSpawnId);
        Adopt(prepared, revision);
    }

    private void Adopt(PreparedState prepared, long revision)
    {
        _layers = prepared.Layers;
        _width = prepared.Width;
        _height = prepared.Height;
        _name = prepared.Name;
        _suggestedPlayers = prepared.SuggestedPlayers;
        _zones = prepared.Zones;
        _spawnPoints = prepared.SpawnPoints;
        _nextZoneId = prepared.NextZoneId;
        _nextSpawnId = prepared.NextSpawnId;
        _revision = revision;
        _savedRevision = revision;
    }

    private static PreparedState Prepare(MapManifest manifest, MapEditorLayers layers,
        long nextZoneId, long nextSpawnId)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Zones);
        ArgumentNullException.ThrowIfNull(manifest.SpawnPoints);
        EnsureMatchingDimensions(layers);

        ImmutableArray<MapEditorZone>.Builder zones = ImmutableArray.CreateBuilder<MapEditorZone>(
            manifest.Zones.Length);
        foreach (MapZoneDef zone in manifest.Zones)
        {
            ArgumentNullException.ThrowIfNull(zone);
            ArgumentNullException.ThrowIfNull(zone.Tags);
            ArgumentNullException.ThrowIfNull(zone.Effects);
            ArgumentNullException.ThrowIfNull(zone.Shape);
            zones.Add(new MapEditorZone(new MapEditorZoneId(checked(nextZoneId++)), zone.Name,
                zone.Tags.ToImmutableArray(), zone.Shape, zone.Effects.ToImmutableArray()));
        }

        ImmutableArray<MapEditorSpawn>.Builder spawns =
            ImmutableArray.CreateBuilder<MapEditorSpawn>(manifest.SpawnPoints.Length);
        foreach (MapSpawnPoint spawn in manifest.SpawnPoints)
        {
            spawns.Add(new MapEditorSpawn(new MapEditorSpawnId(checked(nextSpawnId++)), spawn));
        }

        return new PreparedState(manifest.Name, manifest.SuggestedPlayers,
            zones.MoveToImmutable(), spawns.MoveToImmutable(), layers, layers.Solid.Width,
            layers.Solid.Height, nextZoneId, nextSpawnId);
    }

    private MapEditorUpdate Commit(MapEditorChange change)
    {
        _revision = checked(_revision + 1);
        Snapshot = BuildSnapshot();
        return new MapEditorUpdate(Snapshot, change);
    }

    private MapEditorSnapshot BuildSnapshot()
    {
        ImmutableArray<ContentDiagnostic> diagnostics = MapManifestValidator.Validate(
            BuildManifest(), _validationSource, new MapDimensions(_width, _height))
            .ToImmutableArray();
        return new MapEditorSnapshot(_mapId, _name, _suggestedPlayers, _zones, _spawnPoints,
            _layers, _width, _height, _revision, _savedRevision, diagnostics);
    }

    private MapEditorLayerAsset GetLayer(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => _layers.Background,
        MapEditorLayer.SOLID => _layers.Solid,
        MapEditorLayer.DESTRUCTIBLE => _layers.Destructible,
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

    private static MapEditorOperationFailure RequiredFailure(MapEditorOperationFailure? failure) =>
        failure ?? new MapEditorIoFailure("The map editor store returned no value or failure.");

    private static MapEditorOperationResult MissingStore() =>
        MapEditorOperationResult.Failed(new MapEditorIoFailure(
            "This workspace has no persistence store."));
}
