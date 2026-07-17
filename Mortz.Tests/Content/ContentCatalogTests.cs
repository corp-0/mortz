using System.Text;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Content;

public sealed class ContentCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mortz-content-{Guid.NewGuid():N}");

    [Fact]
    public void PacksSortByLoadOrderThenId()
    {
        AddPack("Zulu", "zulu", 10);
        AddPack("Beta", "beta", 0);
        AddPack("Alpha", "alpha", 0);

        ContentCatalogResult result = ContentCatalog.Load(_root);

        Assert.NotNull(result.Catalog);
        Assert.Equal(["alpha", "beta", "zulu"],
            result.Catalog.Packs.Select(p => p.Manifest.Id).ToArray());
    }

    [Fact]
    public void LaterPackReplacesOneLogicalMapAndKeepsProvenance()
    {
        string basePack = AddPack("Base", "base", 0);
        string modPack = AddPack("Mod", "mod", 100);
        AddMap(basePack, "arena", "Base Arena");
        AddMap(basePack, "duel", "Base Duel");
        AddMap(modPack, "arena", "Mod Arena");

        ContentCatalogResult result = ContentCatalog.Load(_root);

        ContentCatalog catalog = Assert.IsType<ContentCatalog>(result.Catalog);
        Assert.True(catalog.TryGetMap("arena", out ResolvedMapDefinition? arena));
        Assert.Equal("Mod Arena", arena!.Winner.Manifest.Name);
        Assert.Equal(["base", "mod"],
            arena.OverrideChain.Select(m => m.SourcePack.Manifest.Id).ToArray());
        Assert.True(catalog.TryGetMap("duel", out ResolvedMapDefinition? duel));
        Assert.Equal("base", duel!.Winner.SourcePack.Manifest.Id);
        Assert.Single(duel.OverrideChain);
    }

    [Fact]
    public void EqualOrderOverrideIsDeterministicByPackId()
    {
        string zulu = AddPack("Zulu", "zulu", 0);
        string alpha = AddPack("Alpha", "alpha", 0);
        AddMap(zulu, "arena", "Zulu Arena");
        AddMap(alpha, "arena", "Alpha Arena");

        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);

        Assert.True(catalog.TryGetMap("arena", out ResolvedMapDefinition? arena));
        Assert.Equal("Zulu Arena", arena!.Winner.Manifest.Name);
        Assert.Equal(["alpha", "zulu"],
            arena.OverrideChain.Select(m => m.SourcePack.Manifest.Id).ToArray());
    }

    [Fact]
    public void DuplicatePackIdMakesCatalogUnusable()
    {
        AddPack("First", "same", 0);
        AddPack("Second", "same", 10);

        ContentCatalogResult result = ContentCatalog.Load(_root);

        Assert.Null(result.Catalog);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("duplicate content pack id 'same'", StringComparison.Ordinal));
    }

    [Fact]
    public void HashIncludesWinningTomlBytesAndAllLayers()
    {
        string pack = AddPack("Base", "base", 0);
        string mapDirectory = AddMap(pack, "arena", "Arena");
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);
        Assert.True(catalog.TryGetMap("arena", out ResolvedMapDefinition? arena));
        string original = Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(arena!.Winner).Value).CompatibilityHash;

        File.AppendAllText(Path.Combine(mapDirectory, "map.toml"), "# changed\n");
        string manifestChanged = Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(arena.Winner).Value).CompatibilityHash;
        using (FileStream stream = File.Open(Path.Combine(mapDirectory, "solid.png"), FileMode.Append))
            stream.WriteByte(42);
        string layerChanged = Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(arena.Winner).Value).CompatibilityHash;

        Assert.NotEqual(original, manifestChanged);
        Assert.NotEqual(manifestChanged, layerChanged);
        Assert.Equal(4, original.Split(':').Length);
        Assert.All(original.Split(':'), part => Assert.Equal(64, part.Length));
    }

    [Fact]
    public void SourceSnapshotKeepsManifestImagesAndHashOnOneRevision()
    {
        string pack = AddPack("Base", "base", 0);
        string mapDirectory = AddMap(pack, "arena", "Original");
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);
        Assert.True(catalog.TryGetMap("arena", out ResolvedMapDefinition? arena));

        MapSourceSnapshot original = Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(arena!.Winner).Value);
        File.WriteAllText(Path.Combine(mapDirectory, "map.toml"),
            ContentManifestReader.WriteMap(new MapManifest(1, "Changed", 4)));
        File.WriteAllBytes(Path.Combine(mapDirectory, "solid.png"), "changed"u8.ToArray());
        MapSourceSnapshot changed = Assert.IsType<MapSourceSnapshot>(
            MapSourceSnapshot.Read(arena.Winner).Value);

        Assert.Equal("Original", original.Manifest.Name);
        Assert.Equal("Changed", changed.Manifest.Name);
        Assert.NotEqual(original.CompatibilityHash, changed.CompatibilityHash);
        Assert.Equal("solid.png"u8.ToArray(), original.SolidPng.ToArray());
    }

    [Fact]
    public void PackageWriterReplacesOnlyAfterCompleteStagingAndRemovesStaleFiles()
    {
        string mapsDirectory = Path.Combine(_root, "Base", "maps");
        MapPackageWriteRequest first = new("arena", new MapManifest(1, "First", 2),
            "background-1"u8.ToArray(), "solid-1"u8.ToArray(), "dirt-1"u8.ToArray());
        MapPackageWriter.Write(mapsDirectory, first);
        string mapDirectory = Path.Combine(mapsDirectory, "arena");
        File.WriteAllText(Path.Combine(mapDirectory, "stale.txt"), "old");

        MapPackageWriteRequest second = new("arena", new MapManifest(1, "Second", 4),
            "background-2"u8.ToArray(), "solid-2"u8.ToArray(), "dirt-2"u8.ToArray());
        MapPackageWriter.Write(mapsDirectory, second);

        Assert.False(File.Exists(Path.Combine(mapDirectory, "stale.txt")));
        Assert.Equal("Second", Assert.IsType<MapManifest>(ContentManifestReader.ReadMapFile(
            Path.Combine(mapDirectory, "map.toml")).Value).Name);
        Assert.Equal("solid-2"u8.ToArray(), File.ReadAllBytes(Path.Combine(mapDirectory, "solid.png")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Base", ".mortz-transactions")));
    }

    [Fact]
    public void PackageWriterFailureLeavesExistingPackageUntouched()
    {
        string mapsDirectory = Path.Combine(_root, "Base", "maps");
        MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
            "arena", new MapManifest(1, "Good", 2),
            "background"u8.ToArray(), "solid"u8.ToArray(), "dirt"u8.ToArray()));

        Assert.Throws<ArgumentException>(() => MapPackageWriter.Write(mapsDirectory,
            new MapPackageWriteRequest("arena", new MapManifest(1, "", 2),
                "new-background"u8.ToArray(), "new-solid"u8.ToArray(), "new-dirt"u8.ToArray())));

        string manifestPath = Path.Combine(mapsDirectory, "arena", "map.toml");
        Assert.Equal("Good", Assert.IsType<MapManifest>(
            ContentManifestReader.ReadMapFile(manifestPath).Value).Name);
    }

    [Fact]
    public void HashReportsMissingRequiredLayer()
    {
        string pack = AddPack("Base", "base", 0);
        string mapDirectory = AddMap(pack, "arena", "Arena");
        ContentCatalog catalog = Assert.IsType<ContentCatalog>(ContentCatalog.Load(_root).Catalog);
        Assert.True(catalog.TryGetMap("arena", out ResolvedMapDefinition? arena));
        File.Delete(Path.Combine(mapDirectory, "background.png"));

        ContentReadResult<MapSourceSnapshot> snapshot = MapSourceSnapshot.Read(arena!.Winner);

        Assert.Null(snapshot.Value);
        Assert.Contains(snapshot.Diagnostics,
            diagnostic => diagnostic.Message.Contains("background.png", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingContentRootIsExplicit()
    {
        ContentCatalogResult result = ContentCatalog.Load(Path.Combine(_root, "missing"));

        Assert.Null(result.Catalog);
        ContentDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string AddPack(string directoryName, string id, int loadOrder)
    {
        string directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "content_pack.toml"),
            $"id = \"{id}\"\nname = \"{id}\"\nversion = \"1.0.0\"\nload_order = {loadOrder}\n");
        return directory;
    }

    private static string AddMap(string packDirectory, string id, string name)
    {
        string directory = Path.Combine(packDirectory, "maps", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "map.toml"),
            ContentManifestReader.WriteMap(new MapManifest(1, name, 4)));
        foreach (string layer in new[] { "background.png", "solid.png", "destructible.png" })
        {
            File.WriteAllBytes(Path.Combine(directory, layer), Encoding.UTF8.GetBytes(layer));
        }
        return directory;
    }
}
