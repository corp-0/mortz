using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Terrain;
using Mortz.Shared;

namespace Mortz.Client;

internal enum TerrainChunkState
{
    Ignored,
    Waiting,
    Complete,
    Rejected,
}

internal readonly record struct TerrainChunkResult(
    TerrainChunkState State,
    byte[]? Data = null,
    string Error = "");

/// <summary>Validates and assembles one Welcome terrain transfer. It accepts
/// out-of-order chunks, ignores duplicates and unrelated transfer ids, and
/// exposes complete bytes only after the declared length is exact.</summary>
internal sealed class TerrainTransfer
{
    private readonly WelcomeMsg _welcome;
    private readonly byte[]?[] _chunks;
    private int _received;

    public MatchConfig Config { get; }
    public TerrainSyncEncoding Encoding => (TerrainSyncEncoding)_welcome.TerrainEncoding;

    private TerrainTransfer(WelcomeMsg welcome, MatchConfig config)
    {
        _welcome = welcome;
        Config = config;
        _chunks = new byte[welcome.TerrainChunks][];
    }

    public static bool TryCreate(WelcomeMsg welcome, out TerrainTransfer? transfer, out string error)
    {
        transfer = null;
        if (welcome.TerrainEncoding > (byte)TerrainSyncEncoding.CarveLog ||
            welcome.TerrainBytes is < 0 or > NetConfig.MAX_TERRAIN_SYNC_BYTES ||
            welcome.TerrainChunks is < 1 or > NetConfig.MAX_TERRAIN_SYNC_CHUNKS)
        {
            error = "Invalid terrain sync metadata.";
            return false;
        }
        try
        {
            transfer = new TerrainTransfer(welcome, MatchConfig.FromBytes(welcome.Config));
            error = "";
            return true;
        }
        catch (IOException exception)
        {
            error = $"Invalid match config: {exception.Message}";
            return false;
        }
    }

    public TerrainChunkResult Accept(TerrainChunkMsg chunk)
    {
        if (chunk.TransferId != _welcome.TerrainTransferId)
            return new TerrainChunkResult(TerrainChunkState.Ignored);
        if (chunk.Count != _welcome.TerrainChunks || chunk.Index < 0 ||
            chunk.Index >= chunk.Count || chunk.Data.Length > NetConfig.TERRAIN_CHUNK_BYTES)
            return Reject("Invalid terrain sync chunk.");
        if (_chunks[chunk.Index] != null)
            return new TerrainChunkResult(TerrainChunkState.Ignored);

        _chunks[chunk.Index] = chunk.Data;
        _received++;
        if (_received != _chunks.Length)
            return new TerrainChunkResult(TerrainChunkState.Waiting);

        byte[] data = new byte[_welcome.TerrainBytes];
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
            ? new TerrainChunkResult(TerrainChunkState.Complete, data)
            : Reject("Terrain sync length mismatch.");
    }

    private static TerrainChunkResult Reject(string error) =>
        new(TerrainChunkState.Rejected, Error: error);
}

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
