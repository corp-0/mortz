using Mortz.Server.Content;

namespace Mortz.Tests.Server;

/// <summary>Serves the snapshots it was given and nothing else, so an unknown
/// id and an unloadable map are both representable.</summary>
internal sealed class FakeMapSource : IMapSource
{
    private readonly Dictionary<string, MapSnapshot> _maps = new(StringComparer.Ordinal);

    public void Add(MapSnapshot map) => _maps[map.MapId] = map;

    public MapSnapshot? Load(string mapId) =>
        _maps.GetValueOrDefault(mapId);
}
