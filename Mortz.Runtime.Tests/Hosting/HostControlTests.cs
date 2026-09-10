using System.Buffers.Binary;
using Mortz.Protocol.Hosting;
using Xunit;

namespace Mortz.Runtime.Tests.Hosting;

public class HostControlTests
{
    private const string TOKEN = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Theory]
    [InlineData(HostControlKind.READY)]
    [InlineData(HostControlKind.FAILED)]
    [InlineData(HostControlKind.STOP)]
    public async Task MessagesRoundTripAcrossFragmentedReads(HostControlKind kind)
    {
        HostControlMessage message = kind switch
        {
            HostControlKind.READY => new(kind, TOKEN, "127.0.0.1", 30000, -1),
            HostControlKind.FAILED => new(kind, TOKEN, Reason: "Cannot bind game port."),
            _ => new(kind, TOKEN),
        };
        using MemoryStream bytes = new();
        await HostControl.WriteAsync(bytes, message, TestContext.Current.CancellationToken);
        using FragmentedStream fragmented = new(bytes.ToArray());
        Assert.Equal(message, await HostControl.ReadAsync(fragmented, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4097)]
    [InlineData(int.MaxValue)]
    public async Task RejectsFrameLengthBeforeAllocatingPayload(int length)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using MemoryStream stream = new(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => HostControl.ReadAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsTruncatedAndTrailingFrames()
    {
        using MemoryStream frame = new();
        await HostControl.WriteAsync(frame, new(HostControlKind.STOP, TOKEN), TestContext.Current.CancellationToken);
        byte[] bytes = frame.ToArray();
        using MemoryStream truncated = new(bytes[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => HostControl.ReadAsync(truncated, TestContext.Current.CancellationToken));
        byte[] trailing = [.. bytes, 1];
        BinaryPrimitives.WriteInt32LittleEndian(trailing, trailing.Length - 4);
        using MemoryStream extended = new(trailing);
        await Assert.ThrowsAsync<InvalidDataException>(() => HostControl.ReadAsync(extended, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("192.0.2.1", 30000, 30001)]
    [InlineData("127.0.0.1", 0, 30001)]
    [InlineData("127.0.0.1", 30000, 30000)]
    [InlineData("127.0.0.1", 30000, 0)]
    [InlineData("127.0.0.1", 30000, 65536)]
    public async Task RejectsInvalidLocalEndpoints(string address, int game, int query)
    {
        using MemoryStream stream = new();
        await Assert.ThrowsAsync<InvalidDataException>(() => HostControl.WriteAsync(stream,
            new(HostControlKind.READY, TOKEN, address, game, query), TestContext.Current.CancellationToken));
    }

    private class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }
}
