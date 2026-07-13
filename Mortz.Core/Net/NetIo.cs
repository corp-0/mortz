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

    public static byte[] ReadByteArray(this BinaryReader r) => r.ReadBytes(r.ReadInt32());

    public static int[] ReadInt32Array(this BinaryReader r)
    {
        int[] v = new int[r.ReadInt32()];
        for (int i = 0; i < v.Length; i++)
            v[i] = r.ReadInt32();
        return v;
    }

    public static long[] ReadInt64Array(this BinaryReader r)
    {
        long[] v = new long[r.ReadInt32()];
        for (int i = 0; i < v.Length; i++)
            v[i] = r.ReadInt64();
        return v;
    }

    public static string[] ReadStringArray(this BinaryReader r)
    {
        string[] v = new string[r.ReadInt32()];
        for (int i = 0; i < v.Length; i++)
            v[i] = r.ReadString();
        return v;
    }

    public static Vec2 ReadVec2(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());
}
