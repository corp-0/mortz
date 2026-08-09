using Mortz.Content;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorDocument(MapManifest manifest)
{
    private readonly List<MapZoneDef> _zones = [.. manifest.Zones];
    private readonly List<MapSpawnPoint> _spawnPoints = [.. manifest.SpawnPoints];

    public MapManifest Manifest { get; } = manifest;
    public IReadOnlyList<MapZoneDef> Zones => _zones;
    public IReadOnlyList<MapSpawnPoint> SpawnPoints => _spawnPoints;

    public void Add(MapZoneDef zone) => _zones.Add(zone);

    public void Replace(int index, MapZoneDef zone) => _zones[index] = zone;

    public void RemoveAt(int index) => _zones.RemoveAt(index);

    public void AddSpawn(MapSpawnPoint spawn) => _spawnPoints.Add(spawn);

    public void ReplaceSpawn(int index, MapSpawnPoint spawn) => _spawnPoints[index] = spawn;

    public void RemoveSpawnAt(int index) => _spawnPoints.RemoveAt(index);

    public MapManifest BuildManifest() => Manifest with
    {
        Zones = [.. _zones],
        SpawnPoints = [.. _spawnPoints],
    };
}
