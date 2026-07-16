namespace Mortz.Client.Session;

internal readonly record struct TerrainChunkResult(
    TerrainChunkState State,
    byte[]? Data = null,
    string Error = "");
