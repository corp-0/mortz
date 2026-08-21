using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Content;

public sealed class MapPackageWriterAdditionalFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"mortz-additional-files-{Guid.NewGuid():N}");

    [Fact]
    public void AdditionalFilesAreStagedAtomicallyAndWrittenExactly()
    {
        byte[] editor = [0, 1, 2, 255];
        MapPackageWriter.Write(_root, Request(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["editor.json"] = editor,
            ["metadata/notes.bin"] = "notes"u8.ToArray(),
        }));

        Assert.Equal(editor, File.ReadAllBytes(Path.Combine(_root, "test", "editor.json")));
        Assert.Equal("notes"u8.ToArray(), File.ReadAllBytes(
            Path.Combine(_root, "test", "metadata", "notes.bin")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/../../escape")]
    [InlineData("/absolute")]
    [InlineData("background.png")]
    [InlineData("MAP.TOML")]
    public void InvalidOrReservedAdditionalPathsAreRejectedBeforeMutation(string path)
    {
        MapPackageWriter.Write(_root, Request());
        byte[] original = File.ReadAllBytes(Path.Combine(_root, "test", "solid.png"));

        Assert.Throws<ArgumentException>(() => MapPackageWriter.Write(_root,
            Request(new Dictionary<string, ReadOnlyMemory<byte>> { [path] = new byte[] { 9 } })));

        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_root, "test", "solid.png")));
    }

    [Fact]
    public void ReplacementRemovesUnknownExistingFilesByContract()
    {
        MapPackageWriter.Write(_root, Request(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["editor.json"] = "source"u8.ToArray(),
            ["unknown.bin"] = "unknown"u8.ToArray(),
        }));

        MapPackageWriter.Write(_root, Request(new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["editor.json"] = "replacement"u8.ToArray(),
        }));

        Assert.False(File.Exists(Path.Combine(_root, "test", "unknown.bin")));
        Assert.Equal("replacement"u8.ToArray(),
            File.ReadAllBytes(Path.Combine(_root, "test", "editor.json")));
    }

    [Fact]
    public void LargeLayerBuffersAreWrittenWithoutProportionalManagedCopies()
    {
        MapPackageWriter.Write(_root, Request());
        byte[] background = new byte[8 * 1024 * 1024];
        byte[] solid = new byte[8 * 1024 * 1024];
        byte[] destructible = new byte[8 * 1024 * 1024];
        MapPackageWriteRequest request = new("test",
            new MapManifest { Name = "Test", SuggestedPlayers = 1 },
            background, solid, destructible);
        long before = GC.GetAllocatedBytesForCurrentThread();

        MapPackageWriter.Write(_root, request);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4 * 1024 * 1024,
            $"Writer allocated {allocated:N0} bytes for borrowed layer buffers.");
    }

    private static MapPackageWriteRequest Request(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? additional = null) => new(
        "test", new MapManifest { Name = "Test", SuggestedPlayers = 1 },
        "background"u8.ToArray(), "solid"u8.ToArray(), "destructible"u8.ToArray(),
        AdditionalFiles: additional);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
