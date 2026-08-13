using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Sim;
using Mortz.Core.Terrain;

namespace Mortz.Client.Session;

/// <summary>The terrain payload being received for one match load.</summary>
public sealed class TerrainTransfer
{
    private readonly MatchLoadMsg _load;
    private readonly byte[]?[] _chunks;
    private int _received;

    public MatchConfig Config { get; }
    public TerrainSyncEncoding Encoding => (TerrainSyncEncoding)_load.TerrainEncoding;

    private TerrainTransfer(MatchLoadMsg load, MatchConfig config)
    {
        _load = load;
        Config = config;
        _chunks = new byte[load.TerrainChunks][];
    }

    public static bool TryCreate(MatchLoadMsg load, out TerrainTransfer? transfer, out string error)
    {
        transfer = null;
        if (load.TerrainEncoding > (byte)TerrainSyncEncoding.CARVE_LOG ||
            load.TerrainBytes is < 0 or > NetConfig.MAX_TERRAIN_SYNC_BYTES ||
            load.TerrainChunks is < 1 or > NetConfig.MAX_TERRAIN_SYNC_CHUNKS)
        {
            error = "Invalid terrain sync metadata.";
            return false;
        }
        try
        {
            transfer = new TerrainTransfer(load, MatchConfig.FromBytes(load.Config));
            error = "";
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            error = $"Invalid match config: {exception.Message}";
            return false;
        }
    }

    public TerrainChunkResult Accept(TerrainChunkMsg chunk)
    {
        if (chunk.TransferId != _load.TerrainTransferId)
            return new TerrainChunkResult(TerrainChunkState.IGNORED);
        if (chunk.Count != _load.TerrainChunks || chunk.Index < 0 ||
            chunk.Index >= chunk.Count || chunk.Data.Length > NetConfig.TERRAIN_CHUNK_BYTES)
            return Reject("Invalid terrain sync chunk.");
        if (_chunks[chunk.Index] != null)
            return new TerrainChunkResult(TerrainChunkState.IGNORED);

        _chunks[chunk.Index] = chunk.Data;
        _received++;
        if (_received != _chunks.Length)
            return new TerrainChunkResult(TerrainChunkState.WAITING);

        byte[] data = new byte[_load.TerrainBytes];
        int offset = 0;
        foreach (byte[]? part in _chunks)
        {
            if (part == null)
                return Reject("Terrain sync length mismatch.");
            if (offset + part.Length > data.Length)
                return Reject("Terrain sync length mismatch.");
            Buffer.BlockCopy(part, 0, data, offset, part.Length);
            offset += part.Length;
        }
        return offset == data.Length
            ? new TerrainChunkResult(TerrainChunkState.COMPLETE, data)
            : Reject("Terrain sync length mismatch.");
    }

    private static TerrainChunkResult Reject(string error) =>
        new(TerrainChunkState.REJECTED, Error: error);
}
