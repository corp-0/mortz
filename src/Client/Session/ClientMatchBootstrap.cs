using Mortz.Core.Net.Messages;
using Mortz.Shared;

namespace Mortz.Client.Session;

/// <summary>Verified map plus its in-progress terrain/config transfer.</summary>
internal sealed class ClientMatchBootstrap
{
    public MapPackage Map { get; }
    public TerrainTransfer Terrain { get; }

    private ClientMatchBootstrap(MapPackage map, TerrainTransfer terrain)
    {
        Map = map;
        Terrain = terrain;
    }

    public static bool TryCreate(WelcomeMsg welcome, out ClientMatchBootstrap? bootstrap,
        out string error)
    {
        bootstrap = null;
        MapPackage? map = MapPackage.Load(welcome.MapId);
        if (map == null || map.Hash != welcome.MapHash)
        {
            error = $"Map mismatch: {welcome.MapId}";
            return false;
        }
        if (!TerrainTransfer.TryCreate(welcome, out TerrainTransfer? terrain, out error))
            return false;
        bootstrap = new ClientMatchBootstrap(map, terrain!);
        return true;
    }
}
