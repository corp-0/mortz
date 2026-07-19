using System.Collections.Frozen;
using System.Collections.Immutable;
using JetBrains.Annotations;

namespace Mortz.Content;

public sealed record ContentPackDefinition(ContentPackManifest Manifest, string DirectoryPath);

public sealed record MapDefinition(
    [property: UsedImplicitly] string Id,
    MapManifest Manifest,
    string DirectoryPath,
    ContentPackDefinition SourcePack)
{
    public string ManifestPath => Path.Combine(DirectoryPath, "map.toml");
}

public sealed class ResolvedMapDefinition
{
    internal ResolvedMapDefinition(ImmutableArray<MapDefinition> overrideChain) =>
        OverrideChain = overrideChain;

    public ImmutableArray<MapDefinition> OverrideChain { get; }
    public MapDefinition Winner => OverrideChain[^1];
}

public sealed class ContentCatalogResult
{
    internal ContentCatalogResult(ContentCatalog? catalog, IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        Catalog = catalog;
        Diagnostics = diagnostics;
    }

    public ContentCatalog? Catalog { get; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.ERROR);
}

public sealed class ContentCatalog
{
    private readonly FrozenDictionary<string, ResolvedMapDefinition> _maps;

    private ContentCatalog(string rootPath, IReadOnlyList<ContentPackDefinition> packs,
        Dictionary<string, ResolvedMapDefinition> maps)
    {
        RootPath = rootPath;
        Packs = packs.ToImmutableArray();
        _maps = maps.ToFrozenDictionary(StringComparer.Ordinal);
    }

    [UsedImplicitly] public string RootPath { get; }
    public ImmutableArray<ContentPackDefinition> Packs { get; }
    public IReadOnlyDictionary<string, ResolvedMapDefinition> Maps => _maps;

    public bool TryGetMap(string id, out ResolvedMapDefinition? definition) =>
        _maps.TryGetValue(id, out definition);

    public static ContentCatalogResult Load(string contentRoot)
    {
        List<ContentDiagnostic> diagnostics = [];
        string root;
        try
        {
            root = Path.GetFullPath(contentRoot);
            if (!Directory.Exists(root))
            {
                Error(diagnostics, root, "content root does not exist");
                return new ContentCatalogResult(null, diagnostics);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error(diagnostics, contentRoot, $"cannot access content root: {exception.Message}");
            return new ContentCatalogResult(null, diagnostics);
        }

        List<ContentPackDefinition> packs = [];
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(root)
                         .Order(StringComparer.Ordinal))
            {
                string manifestPath = Path.Combine(directory, "content_pack.toml");
                if (!File.Exists(manifestPath))
                    continue;
                ContentReadResult<ContentPackManifest> read = ContentManifestReader.ReadPackFile(manifestPath);
                diagnostics.AddRange(read.Diagnostics);
                if (read.Value is { } manifest)
                    packs.Add(new ContentPackDefinition(manifest, Path.GetFullPath(directory)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(diagnostics, root, $"cannot enumerate content packs: {exception.Message}");
            return new ContentCatalogResult(null, diagnostics);
        }

        packs.Sort((left, right) =>
        {
            int order = left.Manifest.LoadOrder.CompareTo(right.Manifest.LoadOrder);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Manifest.Id, right.Manifest.Id);
        });

        HashSet<string> packIds = new(StringComparer.Ordinal);
        foreach (ContentPackDefinition pack in packs)
        {
            if (!packIds.Add(pack.Manifest.Id))
                Error(diagnostics, pack.DirectoryPath, $"duplicate content pack id '{pack.Manifest.Id}'");
        }
        if (packIds.Count != packs.Count)
            return new ContentCatalogResult(null, diagnostics);

        Dictionary<string, ResolvedMapDefinition> maps = new(StringComparer.Ordinal);
        foreach (ContentPackDefinition pack in packs)
        {
            DiscoverMaps(pack, maps, diagnostics);
        }

        return new ContentCatalogResult(new ContentCatalog(root, packs, maps), diagnostics);
    }

    private static void DiscoverMaps(ContentPackDefinition pack,
        Dictionary<string, ResolvedMapDefinition> maps, List<ContentDiagnostic> diagnostics)
    {
        string mapsDirectory = Path.Combine(pack.DirectoryPath, "maps");
        if (!Directory.Exists(mapsDirectory))
            return;

        IEnumerable<string> mapDirectories;
        try
        {
            mapDirectories = Directory.EnumerateDirectories(mapsDirectory)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(diagnostics, mapsDirectory, $"cannot enumerate maps: {exception.Message}");
            return;
        }

        foreach (string directory in mapDirectories)
        {
            string id = Path.GetFileName(directory);
            if (!ContentManifestReader.IsLogicalId(id))
            {
                Error(diagnostics, directory,
                    $"map directory '{id}' is not a valid logical ID (use lowercase letters, digits, '_' or '-')");
                continue;
            }

            string manifestPath = Path.Combine(directory, "map.toml");
            if (!File.Exists(manifestPath))
                continue;

            ContentReadResult<MapManifest> read = ContentManifestReader.ReadMapFile(manifestPath);
            diagnostics.AddRange(read.Diagnostics);
            if (read.Value is not { } manifest)
                continue;

            MapDefinition definition = new(id, manifest, Path.GetFullPath(directory), pack);
            if (maps.TryGetValue(id, out ResolvedMapDefinition? previous))
                maps[id] = new ResolvedMapDefinition([.. previous.OverrideChain, definition]);
            else
                maps.Add(id, new ResolvedMapDefinition([definition]));
        }
    }

    private static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message));
}
