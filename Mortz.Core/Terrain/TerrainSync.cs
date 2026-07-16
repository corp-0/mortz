using System.IO.Compression;

namespace Mortz.Core.Terrain;

/// <summary>Builds exact late-join terrain state using whichever representation
/// is smaller: compressed carve commands early in a match, or the compressed
/// removed-pixel bitmap after destruction becomes dense.</summary>
public static class TerrainSync
{
    // Bounds decompression work as well as memory. Longer matches fall back to
    // the bitmap, whose apply cost is fixed by map dimensions.
    public const int MAX_CARVES = 4_096;

    public static TerrainSyncPayload Build(TerrainMask mask, IReadOnlyList<TerrainCarve> carves)
    {
        byte[] bitmap = mask.SerializeRemoved();
        if (carves.Count > MAX_CARVES)
            return new TerrainSyncPayload(TerrainSyncEncoding.RemovedBitmap, bitmap);
        byte[] log = SerializeCarves(carves);
        return log.Length < bitmap.Length
            ? new TerrainSyncPayload(TerrainSyncEncoding.CarveLog, log)
            : new TerrainSyncPayload(TerrainSyncEncoding.RemovedBitmap, bitmap);
    }

    public static byte[] SerializeCarves(IReadOnlyList<TerrainCarve> carves)
    {
        if (carves.Count > MAX_CARVES)
            throw new InvalidDataException($"Too many terrain carves: {carves.Count}.");
        using MemoryStream compressed = new();
        using (DeflateStream deflate = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        using (BinaryWriter w = new(deflate))
        {
            w.Write(carves.Count);
            foreach (TerrainCarve carve in carves)
            {
                w.Write(carve.X);
                w.Write(carve.Y);
                w.Write(carve.Radius);
            }
        }
        return compressed.ToArray();
    }

    public static void Apply(TerrainMask mask, TerrainSyncEncoding encoding, byte[] data,
        Action<int, int>? onRemoved = null)
    {
        if (encoding == TerrainSyncEncoding.RemovedBitmap)
        {
            mask.ApplyRemoved(data, onRemoved);
            return;
        }
        if (encoding != TerrainSyncEncoding.CarveLog)
            throw new InvalidDataException($"Unknown terrain encoding {(byte)encoding}.");

        using DeflateStream deflate = new(new MemoryStream(data, writable: false), CompressionMode.Decompress);
        using BinaryReader r = new(deflate);
        int count = r.ReadInt32();
        if (count is < 0 or > MAX_CARVES)
            throw new InvalidDataException($"Invalid terrain carve count {count}.");
        for (int i = 0; i < count; i++)
        {
            int x = r.ReadInt16();
            int y = r.ReadInt16();
            int radius = r.ReadByte();
            foreach ((int px, int py) in mask.CarveCircle(x, y, radius))
                onRemoved?.Invoke(px, py);
        }
        if (deflate.ReadByte() != -1)
            throw new InvalidDataException("Trailing terrain carve data.");
    }
}
