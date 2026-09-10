using Mortz.Protocol.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class PacketEncoderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuresConditionalFieldsAndWritesLittleEndianBytes(bool extended)
    {
        byte[] data = PacketEncoder.Encode((short)-2, extended, Write);

        byte[] expected = [0xAB, 0xFE, 0xFF, 0x34, 0x12, 0x78, 0x56, 0x34, 0x12, 1];
        Assert.Equal(extended ? [.. expected, 0] : expected, data);
    }

    [Fact]
    public void RejectsAWriterThatEmitsFewerBytesOnItsSecondCall()
    {
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => PacketEncoder.Encode(42, false,
            (ref PacketWriter writer, int value, bool _) =>
            {
                writer.Write(value);
                if (++calls == 1)
                    writer.Write(value);
            }));
    }

    [Fact]
    public void EncodingOnlyAllocatesTheOutputArray()
    {
        int size = PacketEncoder.Encode((short)-2, true, Write).Length;

        long outputAllocation = AllocatedBytes(() => new byte[size]);
        long encodingAllocation = AllocatedBytes(() => PacketEncoder.Encode((short)-2, true, Write));

        Assert.Equal(outputAllocation, encodingAllocation);
    }

    private static long AllocatedBytes(Func<byte[]> encode)
    {
        GC.KeepAlive(encode());
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            GC.KeepAlive(encode());
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Write(ref PacketWriter writer, short value, bool extended)
    {
        writer.Write((byte)0xAB);
        writer.Write(value);
        writer.Write((ushort)0x1234);
        writer.Write(0x12345678);
        writer.Write(true);
        if (extended)
            writer.Write(false);
    }
}
