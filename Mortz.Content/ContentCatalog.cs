using System.Collections.Frozen;
using System.Collections.Immutable;
using JetBrains.Annotations;

namespace Mortz.Content;

public sealed record ContentPackDefinition(ContentPackManifest Manifest, string DirectoryPath);

public sealed record ContentDefinition<TManifest>(
    string Id,
    TManifest Manifest,
    string DirectoryPath,
    string ManifestPath,
    ContentPackDefinition SourcePack);

public sealed class ResolvedContent<TManifest>
{
    internal ResolvedContent(ImmutableArray<ContentDefinition<TManifest>> overrideChain) =>
        OverrideChain = overrideChain;

    public ImmutableArray<ContentDefinition<TManifest>> OverrideChain { get; }
    public ContentDefinition<TManifest> Winner => OverrideChain[^1];
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
    private readonly FrozenDictionary<string, ResolvedContent<MapManifest>> _maps;
    private readonly FrozenDictionary<string, ResolvedContent<GameModeManifest>> _modes;

    private ContentCatalog(string rootPath, IReadOnlyList<ContentPackDefinition> packs,
        Dictionary<string, ResolvedContent<MapManifest>> maps,
        Dictionary<string, ResolvedContent<GameModeManifest>> modes)
    {
        RootPath = rootPath;
        Packs = packs.ToImmutableArray();
        _maps = maps.ToFrozenDictionary(StringComparer.Ordinal);
        _modes = modes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    [UsedImplicitly] public string RootPath { get; }
    public ImmutableArray<ContentPackDefinition> Packs { get; }
    public IReadOnlyDictionary<string, ResolvedContent<MapManifest>> Maps => _maps;
    public IReadOnlyDictionary<string, ResolvedContent<GameModeManifest>> Modes => _modes;

    public bool TryGetMap(string id, out ResolvedContent<MapManifest>? definition) =>
        _maps.TryGetValue(id, out definition);

    public bool TryGetMode(string id, out ResolvedContent<GameModeManifest>? definition) =>
        _modes.TryGetValue(id, out definition);

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
                if (read.Value is ContentPackManifest manifest)
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

        Dictionary<string, ResolvedContent<MapManifest>> maps = new(StringComparer.Ordinal);
        Dictionary<string, ResolvedContent<GameModeManifest>> modes = new(StringComparer.Ordinal);
        foreach (ContentPackDefinition pack in packs)
        {
            Discover(pack, "map", ContentManifestReader.ReadMapFile, maps, diagnostics);
            Discover(pack, "mode", ContentManifestReader.ReadModeFile, modes, diagnostics);
        }

        return new ContentCatalogResult(new ContentCatalog(root, packs, maps, modes), diagnostics);
    }

    /// <summary>Content kinds live at &lt;pack&gt;/&lt;kind&gt;s/&lt;id&gt;/&lt;kind&gt;.toml.</summary>
    private static void Discover<TManifest>(ContentPackDefinition pack, string kind,
        Func<string, ContentReadResult<TManifest>> readManifest,
        Dictionary<string, ResolvedContent<TManifest>> definitions,
        List<ContentDiagnostic> diagnostics) where TManifest : class
    {
        string kindDirectory = Path.Combine(pack.DirectoryPath, kind + "s");
        if (!Directory.Exists(kindDirectory))
            return;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(kindDirectory)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error(diagnostics, kindDirectory, $"cannot enumerate {kind}s: {exception.Message}");
            return;
        }

        foreach (string directory in directories)
        {
            string id = Path.GetFileName(directory);
            if (!ContentManifestReader.IsLogicalId(id))
            {
                Error(diagnostics, directory,
                    $"{kind} directory '{id}' is not a valid logical ID (use lowercase letters, digits, '_' or '-')");
                continue;
            }

            string directoryPath = Path.GetFullPath(directory);
            string manifestPath = Path.Combine(directoryPath, kind + ".toml");
            if (!File.Exists(manifestPath))
                continue;

            ContentReadResult<TManifest> read = readManifest(manifestPath);
            diagnostics.AddRange(read.Diagnostics);
            if (read.Value is not TManifest manifest)
                continue;

            ContentDefinition<TManifest> definition = new(id, manifest, directoryPath, manifestPath, pack);
            if (definitions.TryGetValue(id, out ResolvedContent<TManifest>? previous))
                definitions[id] = new ResolvedContent<TManifest>([.. previous.OverrideChain, definition]);
            else
                definitions.Add(id, new ResolvedContent<TManifest>([definition]));
        }
    }

    private static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message));
}
