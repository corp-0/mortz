namespace Mortz.Core.Match.Configuration;

public sealed class MatchConfig
{
    public ModeRules Rules { get; init; } = new();

    public Physics Physics { get; init; } = new();

    public Combat Combat { get; init; } = new();

    public void Clamp()
    {
        Rules.Clamp();
        Physics.Clamp();
        Combat.Clamp();
    }

    // Changing this layout requires a protocol version bump.
    public byte[] ToBytes()
    {
        byte[][] segments = [Rules.ToBytes(), Physics.ToBytes(), Combat.ToBytes()];
        byte[] combined = new byte[segments.Sum(segment => 4 + segment.Length)];
        int offset = 0;
        foreach (byte[] segment in segments)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                combined.AsSpan(offset), segment.Length);
            segment.CopyTo(combined, offset + 4);
            offset += 4 + segment.Length;
        }
        return combined;
    }

    public static MatchConfig FromBytes(byte[] data)
    {
        int offset = 0;
        MatchConfig config = new()
        {
            Rules = ModeRules.FromBytes(ReadSegment(data, ref offset)),
            Physics = Physics.FromBytes(ReadSegment(data, ref offset)),
            Combat = Combat.FromBytes(ReadSegment(data, ref offset)),
        };
        if (offset != data.Length)
            throw new System.IO.InvalidDataException("Trailing bytes in match configuration.");
        return config;
    }

    private static byte[] ReadSegment(byte[] data, ref int offset)
    {
        if (data.Length - offset < 4)
            throw new System.IO.InvalidDataException("Match configuration too short.");
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(offset));
        if (length < 0 || length > data.Length - offset - 4)
            throw new System.IO.InvalidDataException("Match configuration segment length out of range.");
        byte[] segment = data[(offset + 4)..(offset + 4 + length)];
        offset += 4 + length;
        return segment;
    }
}
