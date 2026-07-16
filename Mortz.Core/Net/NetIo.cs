using Mortz.Core.Sim;

namespace Mortz.Core.Net;

/// <summary>
/// Array and Vec2 helpers for generated message serializers; primitives and
/// strings go through BinaryWriter/BinaryReader directly. Messages are rare
/// and small, so no quantization here (that is a snapshot optimization).
/// </summary>
public static class NetIo
{
    public static void WriteArray(this BinaryWriter w, byte[] v)
    {
        w.Write(v.Length);
        w.Write(v);
    }

    public static void WriteArray(this BinaryWriter w, int[] v)
    {
        w.Write(v.Length);
        foreach (int x in v)
            w.Write(x);
    }

    public static void WriteArray(this BinaryWriter w, long[] v)
    {
        w.Write(v.Length);
        foreach (long x in v)
            w.Write(x);
    }

    public static void WriteArray(this BinaryWriter w, string[] v)
    {
        w.Write(v.Length);
        foreach (string x in v)
            w.Write(x);
    }

    public static void Write(this BinaryWriter w, Vec2 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
    }

    public static byte[] ReadByteArray(this BinaryReader r)
    {
        int length = ReadArrayLength(r, sizeof(byte), NetConfig.MAX_BYTE_ARRAY_BYTES);
        byte[] value = r.ReadBytes(length);
        if (value.Length != length)
            throw new EndOfStreamException();
        return value;
    }

    public static int[] ReadInt32Array(this BinaryReader r)
    {
        int[] v = new int[ReadArrayLength(r, sizeof(int), NetConfig.MAX_ARRAY_ELEMENTS)];
        for (int i = 0; i < v.Length; i++)
            v[i] = r.ReadInt32();
        return v;
    }

    public static long[] ReadInt64Array(this BinaryReader r)
    {
        long[] v = new long[ReadArrayLength(r, sizeof(long), NetConfig.MAX_ARRAY_ELEMENTS)];
        for (int i = 0; i < v.Length; i++)
            v[i] = r.ReadInt64();
        return v;
    }

    public static string[] ReadStringArray(this BinaryReader r)
    {
        int count = ReadArrayLength(r, 1, NetConfig.MAX_ARRAY_ELEMENTS);
        string[] v = new string[count];
        for (int i = 0; i < v.Length; i++)
            v[i] = ReadString(r);
        return v;
    }

    public static Vec2 ReadVec2(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());

    /// <summary>Reads BinaryWriter's 7-bit UTF-8 string format with a byte cap.</summary>
    public static string ReadString(BinaryReader r)
    {
        int byteLength = Read7BitEncodedInt(r);
        if (byteLength < 0 || byteLength > NetConfig.MAX_STRING_BYTES)
            throw new InvalidDataException($"Invalid string length {byteLength}.");
        EnsureRemaining(r, byteLength);
        byte[] bytes = r.ReadBytes(byteLength);
        if (bytes.Length != byteLength)
            throw new EndOfStreamException();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static int ReadArrayLength(BinaryReader r, int bytesPerElement, int maxCount)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > maxCount)
            throw new InvalidDataException($"Invalid array length {count}.");
        long required = (long)count * bytesPerElement;
        EnsureRemaining(r, required);
        return count;
    }

    private static void EnsureRemaining(BinaryReader r, long required)
    {
        long remaining = r.BaseStream.Length - r.BaseStream.Position;
        if (required < 0 || required > remaining)
            throw new EndOfStreamException();
    }

    private static int Read7BitEncodedInt(BinaryReader r)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte next = r.ReadByte();
            if (shift == 28 && (next & 0xF0) != 0)
                throw new InvalidDataException("Invalid 7-bit encoded string length.");
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return unchecked((int)value);
        }
        throw new InvalidDataException("Invalid 7-bit encoded string length.");
    }
}
