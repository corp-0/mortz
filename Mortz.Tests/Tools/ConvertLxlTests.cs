using System.IO.Compression;
using System.Text;
using Mortz.Content;
using Mortz.Tools;
using Xunit;

namespace Mortz.Tests.Tools;

public sealed class ConvertLxlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mortz-lxl-{Guid.NewGuid():N}");

    [Fact]
    public void ConverterEmitsCatalogLoadableTomlPackage()
    {
        string packDirectory = Path.Combine(_root, "Base");
        string mapsDirectory = Path.Combine(packDirectory, "maps");
        Directory.CreateDirectory(mapsDirectory);
        File.WriteAllText(Path.Combine(packDirectory, "content_pack.toml"),
            "id = \"base\"\nname = \"Base\"\nversion = \"1\"\nload_order = 0\n");
        string fixture = Path.Combine(_root, "fixture.lxl");
        File.WriteAllBytes(fixture, CreateFixture());

        ConvertLxl.Run([fixture, "converted", "--scale", "1", "--players", "2", "--out", mapsDirectory]);

        string mapDirectory = Path.Combine(mapsDirectory, "converted");
        Assert.True(File.Exists(Path.Combine(mapDirectory, "map.toml")));
        Assert.False(File.Exists(Path.Combine(mapDirectory, "map.json")));
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);
        Assert.True(catalog.TryGetMap("converted", out ResolvedMapDefinition? resolved));
        Assert.Equal("Fixture", resolved!.Winner.Manifest.Name);
        Assert.Equal(2, resolved.Winner.Manifest.SuggestedPlayers);
        Assert.Equal(4, Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(resolved.Winner).Value).CompatibilityHash.Split(':').Length);
    }

    [Theory]
    [InlineData("Bad ID", "mapId")]
    [InlineData("valid", "--scale")]
    public void ConverterRejectsInvalidPackageInputs(string mapId, string expectedMessage)
    {
        string fixture = Path.Combine(_root, "fixture.lxl");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(fixture, CreateFixture());
        string[] args = expectedMessage == "--scale"
            ? [fixture, mapId, "--scale", "0", "--out", _root]
            : [fixture, mapId, "--out", _root];

        Exception error = Assert.Throws<Exception>(() => ConvertLxl.Run(args));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static byte[] CreateFixture()
    {
        byte[] header = new byte[156];
        Encoding.ASCII.GetBytes("LieroX Level").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("Fixture").CopyTo(header, 36);
        BitConverter.GetBytes((uint)1).CopyTo(header, 100); // width
        BitConverter.GetBytes((uint)1).CopyTo(header, 104); // height
        BitConverter.GetBytes((uint)1).CopyTo(header, 108); // image format
        BitConverter.GetBytes((uint)7).CopyTo(header, 152); // decompressed size

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([1, 2, 3, 4, 5, 6, 2]);
        return [.. header, .. compressed.ToArray()];
    }
}
