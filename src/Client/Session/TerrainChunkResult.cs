namespace Mortz.Client.Session;

public readonly record struct TerrainChunkResult(
    TerrainChunkState State,
    byte[]? Data = null,
    string Error = "");
