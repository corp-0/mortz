using System.IO.Compression;
using System.Text;
using Mortz.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mortz.Tools;

/// <summary>Converts OpenLieroX levels into Mortz map packages.</summary>
public static class ConvertLxl
{
    private static readonly Rgba32 _borderColor = new(0x26, 0x26, 0x26);

    public static void Run(string[] args)
    {
        string? lxlPath = null, mapId = null, outRoot = null;
        int scale = 4, players = 4;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scale": scale = int.Parse(args[++i]); break;
                case "--players": players = int.Parse(args[++i]); break;
                case "--out": outRoot = args[++i]; break;
                default:
                    if (lxlPath == null) lxlPath = args[i];
                    else if (mapId == null) mapId = args[i];
                    else throw new Exception($"unexpected argument '{args[i]}'");
                    break;
            }
        }
        if (lxlPath == null || mapId == null)
            throw new Exception("usage: convert-lxl <path.lxl> <mapId> [--scale N] [--players N] [--out DIR]");
        if (!ContentId.IsValid(mapId))
            throw new Exception($"bad mapId '{mapId}': lowercase letters, digits, '_' and '-' only, starting with a letter or digit");
        if (scale <= 0)
            throw new Exception("--scale must be positive");
        if (players <= 0)
            throw new Exception("--players must be positive");
        outRoot ??= Path.Combine(Program.RepoRoot(), "content", "Base", "maps");

        byte[] bytes = File.ReadAllBytes(lxlPath);
        string id = Encoding.ASCII.GetString(bytes, 0, 12);
        if (id != "LieroX Level")
            throw new Exception($"not a LieroX level: '{id}'");
        // Name strings carry uninitialized garbage after the NUL.
        string name = Encoding.ASCII.GetString(bytes, 36, 64).Split('\0')[0];
        int w = (int)BitConverter.ToUInt32(bytes, 100);
        int h = (int)BitConverter.ToUInt32(bytes, 104);
        uint type = BitConverter.ToUInt32(bytes, 108);
        if (type != 1)
            throw new Exception($"only image-format levels supported (type={type})");
        int destSize = (int)BitConverter.ToUInt32(bytes, 152);
        Console.WriteLine($"'{name}' {w}x{h}, output scale {scale}x");

        using MemoryStream ms = new MemoryStream(bytes);
        ms.Position = 156;
        using ZLibStream zlib = new ZLibStream(ms, CompressionMode.Decompress);
        byte[] data = new byte[destSize];
        int read = zlib.ReadAtLeast(data, destSize, throwOnEndOfStream: false);
        if (read < w * h * 7)
            throw new Exception($"decompressed {read} bytes, expected at least {w * h * 7}");

        LayerBytes layers = WriteLayers(data, w, h, scale);
        MapPackageWriter.Write(outRoot, new MapPackageWriteRequest(
            mapId,
            new MapManifest { Name = name, SuggestedPlayers = players },
            layers.Background,
            layers.Solid,
            layers.Destructible));

        Console.WriteLine($"wrote {Path.Combine(outRoot, mapId)}");
    }

    // LXL stores back RGB, front RGB, then one material byte per pixel.
    private static LayerBytes WriteLayers(byte[] data, int w, int h, int scale)
    {
        int pixels = w * h;
        int ow = w * scale;
        int oh = h * scale;
        Rgba32[] back = new Rgba32[ow * oh];
        Rgba32[] solid = new Rgba32[ow * oh];
        Rgba32[] dirt = new Rgba32[ow * oh];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Rgba32 backColor = Rgb(data, i * 3);
                Rgba32 frontColor = Rgb(data, pixels * 3 + i * 3);
                byte mat = data[pixels * 6 + i];
                for (int sy = 0; sy < scale; sy++)
                {
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int o = (y * scale + sy) * ow + x * scale + sx;
                        back[o] = backColor;
                        if ((mat & 4) != 0) solid[o] = frontColor;
                        else if ((mat & 2) != 0) dirt[o] = frontColor;
                    }
                }
            }
        }

        // Keep players and rope hooks inside; leave authored death pits open.
        int border = 4 * scale;
        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < ow; x++)
            {
                if (y >= border && x >= border && x < ow - border)
                    continue;
                int o = y * ow + x;
                solid[o] = _borderColor;
                dirt[o] = default; // transparent
            }
        }

        return new LayerBytes(
            EncodePng(back, ow, oh),
            EncodePng(solid, ow, oh),
            EncodePng(dirt, ow, oh));
    }

    private static Rgba32 Rgb(byte[] data, int ofs) => new(data[ofs], data[ofs + 1], data[ofs + 2]);

    private static byte[] EncodePng(Rgba32[] rgba, int w, int h)
    {
        using Image<Rgba32> image = Image.LoadPixelData(rgba, w, h);
        using MemoryStream stream = new();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private readonly record struct LayerBytes(byte[] Background, byte[] Solid, byte[] Destructible);
}
