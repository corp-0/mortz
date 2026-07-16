namespace Mortz.Core.Terrain;

public readonly record struct TerrainSyncPayload(TerrainSyncEncoding Encoding, byte[] Data);
