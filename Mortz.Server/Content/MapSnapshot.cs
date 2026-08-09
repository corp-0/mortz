using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;

namespace Mortz.Server.Content;

/// <summary>
/// Everything the server needs from a map, and nothing else: the decoded PNG
/// layers stay on the client side of the engine seam.
/// </summary>
public sealed record MapSnapshot
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required SpawnPoint[] SpawnPoints { get; init; }
    public MapZones Zones { get; init; } = MapZones.None;
    public required TerrainMask InitialTerrain { get; init; }

    public TerrainMask BuildMask() => InitialTerrain.Copy();
}
