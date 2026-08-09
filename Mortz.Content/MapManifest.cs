using Mortz.Core.Match.Teams;

namespace Mortz.Content;

public readonly record struct MapSpawnPoint(int X, int Y, Team? Team = null);

[TomlModel]
public sealed record MapManifest
{
    public required string Name { get; init; }
    public required int SuggestedPlayers { get; init; }
    public MapSpawnPoint[] SpawnPoints { get; init; } = [];
    public MapZoneDef[] Zones { get; init; } = [];
}
