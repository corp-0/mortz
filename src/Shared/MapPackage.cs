using System.Collections.Immutable;
using Godot;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;

namespace Mortz.Shared;

/// <summary>
/// A loaded map: three PNG layers + manifest. Background never collides; Solid
/// collides and is indestructible; Destructible collides and is carvable. The
/// reading and decoding happens in MapPackageLoader; this is the result.
/// </summary>
public sealed class MapPackage
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required int SuggestedPlayers { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required ImmutableArray<Vec2> SpawnPoints { get; init; }
    public required Image Background { get; init; }
    public required Image Solid { get; init; }
    public required Image Destructible { get; init; }
    internal TerrainMask InitialTerrain { get; init; } = null!;

    public TerrainMask BuildMask() => InitialTerrain.Copy();
}
