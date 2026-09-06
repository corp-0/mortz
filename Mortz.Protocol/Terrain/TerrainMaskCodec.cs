using System.IO.Compression;
using Mortz.Core.Terrain;

namespace Mortz.Protocol.Terrain;

public static class TerrainMaskCodec
{
    public static byte[] SerializeRemoved(this TerrainMask mask)
    {
        byte[] bits = new byte[(mask.Width * mask.Height + 7) / 8];
        for (int i = 0; i < mask.Width * mask.Height; i++)
        {
            if (mask.WasRemoved(i % mask.Width, i / mask.Width))
                bits[i / 8] |= (byte)(1 << (i % 8));
        }
        using MemoryStream stream = new();
        using (DeflateStream deflate = new(stream, CompressionLevel.Fastest))
            deflate.Write(bits);
        return stream.ToArray();
    }

    public static void ApplyRemoved(this TerrainMask mask, byte[] data, Action<int, int>? onRemoved = null)
    {
        byte[] bits = new byte[(mask.Width * mask.Height + 7) / 8];
        using (DeflateStream deflate = new(new MemoryStream(data), CompressionMode.Decompress))
        {
            deflate.ReadExactly(bits);
            if (deflate.ReadByte() != -1)
                throw new InvalidDataException("Trailing terrain bitmap data.");
        }
        for (int i = 0; i < mask.Width * mask.Height; i++)
        {
            int x = i % mask.Width;
            int y = i / mask.Width;
            if ((bits[i / 8] & (1 << (i % 8))) != 0 && mask.RemoveDestructible(x, y))
                onRemoved?.Invoke(x, y);
        }
    }
}
