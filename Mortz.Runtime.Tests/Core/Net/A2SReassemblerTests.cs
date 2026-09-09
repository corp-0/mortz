using System.Buffers.Binary;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class A2SReassemblerTests
{
    [Fact]
    public void CompressedResponse_DecodesIndependentBzip2AndCrcFixture()
    {
        byte[] packet = Convert.FromHexString(
            "FEFFFFFF07000080010078050B000000F6D8D2B7" +
            "425A683931415926535930915C93000005C580E0000200000801000000A00022341FAA0C019C225F8BB9229C28481848AE4980");
        Assert.True(new A2SReassembler().TryAdd(packet, out byte[] response));
        Assert.Equal(Convert.FromHexString("FFFFFFFF4501006B007600"), response);
    }

    [Fact]
    public void SplitResponses_ReassembleOutOfOrderAndIgnoreIdenticalDuplicates()
    {
        byte[] response = LargeRules();
        IReadOnlyList<byte[]> fragments = ServerQueryProtocol.SplitResponse(response, 123);
        Assert.All(fragments, fragment => Assert.InRange(fragment.Length, 13, 1400));
        A2SReassembler assembler = new();
        Assert.False(assembler.TryAdd(fragments[^1], out _));
        Assert.False(assembler.TryAdd(fragments[^1], out _));
        for (int i = fragments.Count - 2; i > 0; i--)
        {
            Assert.False(assembler.TryAdd(fragments[i], out _));
        }
        Assert.True(assembler.TryAdd(fragments[0], out byte[] result));
        Assert.Equal(response, result);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void SplitResponses_RejectChangedIdentityCountSizeOrDuplicatePayload(int offset)
    {
        IReadOnlyList<byte[]> fragments = ServerQueryProtocol.SplitResponse(LargeRules(), 123);
        A2SReassembler assembler = new();
        Assert.False(assembler.TryAdd(fragments[0], out _));
        byte[] conflicting = fragments[0].ToArray();
        conflicting[offset] ^= 1;
        Assert.False(assembler.TryAdd(conflicting, out _));
        Assert.True(assembler.Rejected);
    }

    [Fact]
    public void SplitResponses_RejectTooManyFragmentsAndOutOfRangeIndex()
    {
        byte[] fragment = ServerQueryProtocol.SplitResponse(LargeRules(), 123)[0];
        fragment[8] = 65;
        Assert.False(new A2SReassembler().TryAdd(fragment, out _));
        fragment[8] = 2;
        fragment[9] = 2;
        Assert.False(new A2SReassembler().TryAdd(fragment, out _));
    }

    [Fact]
    public void SplitResponses_AcceptLegalInboundDatagramsLargerThanOutboundLimit()
    {
        byte[] rules = LargeRules();
        byte[] packet = Fragment(rules, 7, 1, 0, 65535);
        Assert.True(packet.Length > 1400);
        Assert.True(new A2SReassembler().TryAdd(packet, out byte[] actual));
        Assert.Equal(rules, actual);
    }

    [Fact]
    public void SplitResponses_BoundCombinedPayloadBeforeAllocating()
    {
        A2SReassembler assembler = new();
        Assert.False(assembler.TryAdd(Fragment(new byte[40000], 7, 2, 0, 65535), out _));
        Assert.False(assembler.TryAdd(Fragment(new byte[40000], 7, 2, 1, 65535), out _));
        Assert.True(assembler.Rejected);
    }

    [Fact]
    public void CompressedResponses_VerifyLengthChecksumAndOutOfOrderFragments()
    {
        byte[] expected = LargeRules();
        byte[] compressed = Compress(expected);
        int half = compressed.Length / 2;
        byte[] first = CompressedFragment(compressed[..half], expected, 2, 0);
        byte[] second = Fragment(compressed[half..], 0x80000007, 2, 1, 1400);
        A2SReassembler assembler = new();
        Assert.False(assembler.TryAdd(second, out _));
        Assert.True(assembler.TryAdd(first, out byte[] result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(21)]
    public void CompressedResponses_RejectWrongLengthChecksumAndInvalidStream(int offset)
    {
        byte[] expected = LargeRules();
        byte[] packet = CompressedFragment(Compress(expected), expected, 1, 0);
        packet[offset] ^= 1;
        Assert.False(new A2SReassembler().TryAdd(packet, out _));
    }

    [Fact]
    public void CompressedResponses_RejectOversizedDecompressedLength()
    {
        byte[] expected = LargeRules();
        byte[] packet = CompressedFragment(Compress(expected), expected, 1, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 65537);
        Assert.False(new A2SReassembler().TryAdd(packet, out _));
    }

    [Fact]
    public void MalformedPackets_DoNotThrow()
    {
        Random random = new(123);
        for (int i = 0; i < 500; i++)
        {
            byte[] packet = new byte[random.Next(0, 2000)];
            random.NextBytes(packet);
            new A2SReassembler().TryAdd(packet, out _);
            ServerQueryProtocol.TryDecodeInfo(packet, 7777, out _);
            ServerQueryProtocol.TryDecodeRules(packet, out _);
            ServerQueryProtocol.TryDecodeRequest(packet, out _, out _);
        }
    }

    private static byte[] LargeRules() => ServerQueryProtocol.EncodeRulesResponse(
        Enumerable.Range(0, 20).ToDictionary(index => $"rule{index}", _ => new string('x', 200)));

    private static byte[] Fragment(byte[] payload, uint id, byte count, byte index, ushort size)
    {
        byte[] packet = new byte[12 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, -2);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), id);
        packet[8] = count;
        packet[9] = index;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(10), size);
        payload.CopyTo(packet, 12);
        return packet;
    }

    private static byte[] CompressedFragment(byte[] payload, byte[] decompressed, byte count, byte index)
    {
        byte[] data = new byte[payload.Length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)decompressed.Length);
        Crc32 checksum = new();
        checksum.Update(decompressed);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)checksum.Value);
        payload.CopyTo(data, 8);
        return Fragment(data, 0x80000007, count, index, 1400);
    }

    private static byte[] Compress(byte[] source)
    {
        using MemoryStream input = new(source);
        using MemoryStream output = new();
        BZip2.Compress(input, output, false, 1);
        return output.ToArray();
    }
}
