using Mortz.Client;
using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Client;

public class TerrainTransferTests
{
    private static WelcomeMsg Welcome(int bytes = 5, short chunks = 2, byte[]? config = null) =>
        new("map", "hash", config ?? new MatchConfig().ToBytes(),
            (byte)TerrainSyncEncoding.CarveLog, 17, bytes, chunks);

    [Fact]
    public void OutOfOrderChunksProduceTheDeclaredPayload()
    {
        Assert.True(TerrainTransfer.TryCreate(Welcome(), out TerrainTransfer? transfer, out _));

        TerrainChunkResult second = transfer!.Accept(new TerrainChunkMsg(17, 1, 2, [4, 5]));
        TerrainChunkResult first = transfer.Accept(new TerrainChunkMsg(17, 0, 2, [1, 2, 3]));

        Assert.Equal(TerrainChunkState.Waiting, second.State);
        Assert.Equal(TerrainChunkState.Complete, first.State);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, first.Data);
    }

    [Fact]
    public void DuplicateAndUnrelatedChunksAreIgnored()
    {
        TerrainTransfer.TryCreate(Welcome(), out TerrainTransfer? transfer, out _);
        TerrainChunkMsg first = new(17, 0, 2, [1, 2, 3]);

        Assert.Equal(TerrainChunkState.Waiting, transfer!.Accept(first).State);
        Assert.Equal(TerrainChunkState.Ignored, transfer.Accept(first).State);
        Assert.Equal(TerrainChunkState.Ignored,
            transfer.Accept(new TerrainChunkMsg(99, 1, 2, [4, 5])).State);
    }

    [Fact]
    public void DeclaredLengthMustMatchCompletedChunks()
    {
        TerrainTransfer.TryCreate(Welcome(bytes: 4, chunks: 1),
            out TerrainTransfer? transfer, out _);

        TerrainChunkResult result = transfer!.Accept(new TerrainChunkMsg(17, 0, 1, [1, 2, 3]));

        Assert.Equal(TerrainChunkState.Rejected, result.State);
        Assert.Equal("Terrain sync length mismatch.", result.Error);
    }

    [Fact]
    public void InvalidConfigIsRejectedBeforeChunksAreAccepted()
    {
        bool created = TerrainTransfer.TryCreate(Welcome(config: [1, 2]),
            out TerrainTransfer? transfer, out string error);

        Assert.False(created);
        Assert.Null(transfer);
        Assert.StartsWith("Invalid match config:", error);
    }
}
