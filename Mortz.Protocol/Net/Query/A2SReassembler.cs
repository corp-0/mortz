using System.Buffers.Binary;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;

namespace Mortz.Protocol.Net.Query;

/// <summary>One Source response, bounded before allocating or decompressing its payload.</summary>
public class A2SReassembler
{
    private byte[]?[]? _fragments;
    private uint _requestId;
    private ushort _splitSize;
    private int _received;
    private int _bytes;
    private int _decompressedLength;
    private uint _checksum;
    public bool Rejected { get; private set; }

    public bool TryAdd(ReadOnlySpan<byte> packet, out byte[] response)
    {
        response = [];
        if (Rejected)
            return false;
        if (packet.Length is < 5 or > ServerQueryProtocol.MAX_RESPONSE_BYTES)
            return Reject();
        int header = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (header == -1)
        {
            if (_fragments != null)
                return Reject();
            response = packet.ToArray();
            return true;
        }
        if (header != -2 || packet.Length < 13)
            return Reject();
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        int count = packet[8];
        int index = packet[9];
        ushort splitSize = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]);
        bool compressed = (id & 0x80000000) != 0;
        int offset = compressed && index == 0 ? 20 : 12;
        if (count is < 1 or > ServerQueryProtocol.MAX_FRAGMENTS || index >= count ||
            splitSize == 0 || packet.Length <= offset || packet.Length - 12 > splitSize)
            return Reject();
        if (_fragments == null)
        {
            _fragments = new byte[count][];
            _requestId = id;
            _splitSize = splitSize;
        }
        else if (_requestId != id || _fragments.Length != count || _splitSize != splitSize)
            return Reject();
        if (compressed && index == 0)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]);
            if (length is < 5 or > ServerQueryProtocol.MAX_RESPONSE_BYTES ||
                _fragments[0] != null && (_decompressedLength != length || _checksum != checksum))
                return Reject();
            _decompressedLength = (int)length;
            _checksum = checksum;
        }
        ReadOnlySpan<byte> payload = packet[offset..];
        if (_fragments[index] is { } existing)
            return !existing.AsSpan().SequenceEqual(payload) && Reject();
        if (_bytes + payload.Length > ServerQueryProtocol.MAX_RESPONSE_BYTES)
            return Reject();
        _fragments[index] = payload.ToArray();
        _bytes += payload.Length;
        if (++_received != count)
            return false;
        byte[] joined = new byte[_bytes];
        int cursor = 0;
        foreach (byte[]? fragment in _fragments)
        {
            fragment!.CopyTo(joined, cursor);
            cursor += fragment.Length;
        }
        if (compressed)
        {
            if (!TryDecompress(joined, out joined))
                return Reject();
        }
        // Some Source servers put a second connectionless header inside the payload.
        if (joined.Length >= 8 && BinaryPrimitives.ReadInt32LittleEndian(joined) == -1 &&
            BinaryPrimitives.ReadInt32LittleEndian(joined.AsSpan(4)) == -1)
            joined = joined[4..];
        if (joined.Length < 5 || BinaryPrimitives.ReadInt32LittleEndian(joined) != -1)
            return Reject();
        response = joined;
        return true;
    }

    private bool TryDecompress(byte[] compressed, out byte[] response)
    {
        response = [];
        try
        {
            using MemoryStream source = new(compressed, writable: false);
            using BZip2InputStream input = new(source);
            byte[] output = new byte[_decompressedLength];
            int total = 0;
            while (total < output.Length)
            {
                int read = input.Read(output, total, output.Length - total);
                if (read == 0)
                    return false;
                total += read;
            }
            if (input.ReadByte() != -1 || source.Position != source.Length)
                return false;
            Crc32 crc = new();
            crc.Update(output);
            if ((uint)crc.Value != _checksum)
                return false;
            response = output;
            return true;
        }
        catch (Exception error) when (error is IOException or SharpZipBaseException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private bool Reject()
    {
        Rejected = true;
        _fragments = null;
        return false;
    }
}
