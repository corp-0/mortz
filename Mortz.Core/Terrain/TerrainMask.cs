using System.IO.Compression;

namespace Mortz.Core.Terrain;

/// <summary>
/// The collision side of the terrain: one material cell per pixel. Every peer
/// holds its own copy, changed only by carves; the same carves in the same
/// order must produce byte-identical masks everywhere, so integer math only.
/// Out-of-bounds is empty; maps bring their own solid borders.
/// </summary>
public sealed class TerrainMask
{
    public int Width { get; }
    public int Height { get; }

    private readonly TerrainMaterial[] _cells;
    // Pristine state, for computing the "removed" diff sent to late joiners.
    private readonly TerrainMaterial[] _original;

    public TerrainMask(int width, int height, Func<int, int, bool> solid, Func<int, int, bool> destructible)
    {
        Width = width;
        Height = height;
        _cells = new TerrainMaterial[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Solid wins where layers overlap.
                if (solid(x, y)) _cells[y * width + x] = TerrainMaterial.SOLID;
                else if (destructible(x, y)) _cells[y * width + x] = TerrainMaterial.DESTRUCTIBLE;
            }
        }

        _original = (TerrainMaterial[])_cells.Clone();
    }

    private TerrainMask(int width, int height, TerrainMaterial[] cells, TerrainMaterial[] original)
    {
        Width = width;
        Height = height;
        _cells = cells;
        _original = original;
    }

    /// <summary>Detached copy: carves on it don't touch the original.</summary>
    public TerrainMask Copy() => new(Width, Height,
        (TerrainMaterial[])_cells.Clone(), (TerrainMaterial[])_original.Clone());

    public TerrainMaterial Get(int x, int y) =>
        InBounds(x, y) ? _cells[y * Width + x] : TerrainMaterial.EMPTY;

    public bool IsSolid(int x, int y) => Get(x, y) != TerrainMaterial.EMPTY;

    /// <summary>Any solid cell in the pixel rect [minX,maxX) x [minY,maxY)?</summary>
    public bool RectSolid(float minX, float minY, float maxX, float maxY)
    {
        int x0 = (int)MathF.Floor(minX);
        int x1 = (int)MathF.Ceiling(maxX) - 1;
        int y0 = (int)MathF.Floor(minY);
        int y1 = (int)MathF.Ceiling(maxY) - 1;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (IsSolid(x, y))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Remove Destructible cells within the circle. Returns the removed pixel
    /// coordinates in deterministic (row-major) order; Solid is untouched.
    /// </summary>
    public List<(int X, int Y)> CarveCircle(int cx, int cy, int radius)
    {
        List<(int, int)> removed = new List<(int, int)>();
        int r2 = radius * radius;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (!InBounds(x, y)) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                if (_cells[y * Width + x] != TerrainMaterial.DESTRUCTIBLE) continue;
                _cells[y * Width + x] = TerrainMaterial.EMPTY;
                removed.Add((x, y));
            }
        }

        return removed;
    }

    /// <summary>Undo one pixel of a predicted carve: back to Destructible if
    /// the pristine map had it so. Client prediction only; the authoritative
    /// mask never restores.</summary>
    public void RestoreDestructible(int x, int y)
    {
        if (!InBounds(x, y))
            return;
        int i = y * Width + x;
        if (_original[i] == TerrainMaterial.DESTRUCTIBLE && _cells[i] == TerrainMaterial.EMPTY)
            _cells[i] = TerrainMaterial.DESTRUCTIBLE;
    }

    /// <summary>
    /// Late-join sync: which originally-Destructible cells are now Empty,
    /// as a deflate-compressed 1-bit-per-pixel mask (mostly zeros).
    /// </summary>
    public byte[] SerializeRemoved()
    {
        byte[] bits = new byte[(_cells.Length + 7) / 8];
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_original[i] == TerrainMaterial.DESTRUCTIBLE && _cells[i] == TerrainMaterial.EMPTY)
                bits[i / 8] |= (byte)(1 << (i % 8));
        }

        using MemoryStream ms = new MemoryStream();
        using (DeflateStream deflate = new DeflateStream(ms, CompressionLevel.Fastest))
            deflate.Write(bits);
        return ms.ToArray();
    }

    /// <summary>Apply a removed-mask from <see cref="SerializeRemoved"/>; reports each removed pixel.</summary>
    public void ApplyRemoved(byte[] data, Action<int, int>? onRemoved = null)
    {
        byte[] bits = new byte[(_cells.Length + 7) / 8];
        using (DeflateStream deflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
            deflate.ReadExactly(bits);

        for (int i = 0; i < _cells.Length; i++)
        {
            if ((bits[i / 8] & (1 << (i % 8))) == 0) continue;
            if (_cells[i] != TerrainMaterial.DESTRUCTIBLE) continue;
            _cells[i] = TerrainMaterial.EMPTY;
            onRemoved?.Invoke(i % Width, i / Width);
        }
    }

    private bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
}
