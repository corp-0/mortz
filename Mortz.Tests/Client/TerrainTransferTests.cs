using Mortz.Client.Session;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Net;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Client;

public class TerrainTransferTests
{
    private static MatchLoadMsg MatchLoad(int bytes = 5, short chunks = 2, byte[]? config = null) =>
        new("map", "hash", config ?? new MatchConfig().ToBytes(),
            (byte)TerrainSyncEncoding.CARVE_LOG, 17, bytes, chunks,
            MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1,
            new Snapshot(0, [], []).SerializeFor(1), -1);

    [Fact]
    public void OutOfOrderChunksProduceTheDeclaredPayload()
    {
        Assert.True(TerrainTransfer.TryCreate(MatchLoad(), out TerrainTransfer? transfer, out _));

        TerrainChunkResult second = transfer!.Accept(new TerrainChunkMsg(17, 1, 2, [4, 5]));
        TerrainChunkResult first = transfer.Accept(new TerrainChunkMsg(17, 0, 2, [1, 2, 3]));

        Assert.Equal(TerrainChunkState.WAITING, second.State);
        Assert.Equal(TerrainChunkState.COMPLETE, first.State);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, first.Data);
    }

    [Fact]
    public void DuplicateAndUnrelatedChunksAreIgnored()
    {
        TerrainTransfer.TryCreate(MatchLoad(), out TerrainTransfer? transfer, out _);
        TerrainChunkMsg first = new(17, 0, 2, [1, 2, 3]);

        Assert.Equal(TerrainChunkState.WAITING, transfer!.Accept(first).State);
        Assert.Equal(TerrainChunkState.IGNORED, transfer.Accept(first).State);
        Assert.Equal(TerrainChunkState.IGNORED,
            transfer.Accept(new TerrainChunkMsg(99, 1, 2, [4, 5])).State);
    }

    [Fact]
    public void DeclaredLengthMustMatchCompletedChunks()
    {
        TerrainTransfer.TryCreate(MatchLoad(bytes: 4, chunks: 1),
            out TerrainTransfer? transfer, out _);

        TerrainChunkResult result = transfer!.Accept(new TerrainChunkMsg(17, 0, 1, [1, 2, 3]));

        Assert.Equal(TerrainChunkState.REJECTED, result.State);
        Assert.Equal("Terrain sync length mismatch.", result.Error);
    }

    [Fact]
    public void InvalidConfigIsRejectedBeforeChunksAreAccepted()
    {
        bool created = TerrainTransfer.TryCreate(MatchLoad(config: [1, 2]),
            out TerrainTransfer? transfer, out string error);

        Assert.False(created);
        Assert.Null(transfer);
        Assert.StartsWith("Invalid match config:", error);
    }
}
