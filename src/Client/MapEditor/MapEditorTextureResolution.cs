using Godot;
using Mortz.Content;
using Mortz.Shared;

namespace Mortz.Client.MapEditor;

public enum MapEditorTextureResolutionStatus
{
    RESOLVED,
    MISSING,
    LOAD_ERROR,
}

public sealed class MapEditorTextureData
{
    private readonly byte[] _rgba;

    public MapEditorTextureData(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA data length does not match its dimensions.", nameof(rgba));
        Width = width;
        Height = height;
        _rgba = rgba.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Rgba => _rgba.ToArray();

    internal ReadOnlySpan<byte> Pixels => _rgba;

    public static MapEditorTextureData Solid(MapEditorColor color) =>
        new(1, 1, [color.Red, color.Green, color.Blue, color.Alpha]);
}

public sealed record MapEditorTextureCatalogItem(
    MapEditorTextureReference Reference,
    string SourceName,
    string Name);

public sealed record MapEditorTextureResolution(
    MapEditorTextureResolutionStatus Status,
    MapEditorTextureReference Reference,
    MapEditorTextureData? Texture,
    string Message,
    string? ResolvedPath = null)
{
    public bool IsResolved => Status == MapEditorTextureResolutionStatus.RESOLVED &&
        Texture != null;
}

public interface IMapEditorTextureResolver
{
    MapEditorTextureResolution Resolve(MapEditorTextureReference reference);

    void Invalidate();
}

public enum MapEditorTextureLoadStatus
{
    RESOLVED,
    MISSING,
    LOAD_ERROR,
}

public sealed record MapEditorTextureLoadResult(
    MapEditorTextureLoadStatus Status,
    MapEditorTextureData? Texture = null,
    string? Message = null);

public interface IMapEditorTextureAccess
{
    MapEditorTextureLoadResult Load(MapEditorTextureReference reference);
}

public class GodotMapEditorTextureAccess : IMapEditorTextureAccess
{
    public MapEditorTextureLoadResult Load(MapEditorTextureReference reference)
    {
        if (reference.Source != MapEditorTextureReference.PROJECT_SOURCE)
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.MISSING);
        string resourcePath = reference.Location;
        try
        {
            if (!ResourceLoader.Exists(resourcePath))
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.MISSING);
            Texture2D? texture = ResourceLoader.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"This file isn't a texture: {resourcePath}");
            }
            using Image image = texture.GetImage();
            if (image.IsEmpty())
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"This texture can't be read: {resourcePath}");
            }
            if (image.IsCompressed() && image.Decompress() != Error.Ok)
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"This texture can't be opened: {resourcePath}");
            }
            if (image.GetFormat() != Image.Format.Rgba8)
                image.Convert(Image.Format.Rgba8);
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.RESOLVED,
                new MapEditorTextureData(image.GetWidth(), image.GetHeight(), image.GetData()));
        }
        catch (Exception exception)
        {
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                Message: $"Couldn't open {resourcePath}: {exception.Message}");
        }
    }
}

public class UnavailableMapEditorTextureAccess : IMapEditorTextureAccess
{
    public MapEditorTextureLoadResult Load(MapEditorTextureReference reference) =>
        new(MapEditorTextureLoadStatus.MISSING);
}

public class MapEditorTextureSourceRegistry : IMapEditorTextureAccess
{
    public const string PROJECT_TEXTURE_ROOT = "res://official/Assets/Textures";

    private readonly IMapEditorTextureAccess _project;
    private readonly Dictionary<string, string> _libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _libraryNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _textureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".svg", ".tga", ".bmp",
    };

    public MapEditorTextureSourceRegistry(IMapEditorTextureAccess? project = null)
    {
        _project = project ?? new GodotMapEditorTextureAccess();
    }

    public IReadOnlyDictionary<string, string> Libraries => _libraries;

    public static MapEditorTextureSourceRegistry CreateDefault(
        ContentDefinition<MapManifest>? definition = null)
    {
        MapEditorTextureSourceRegistry registry = new();
        if (definition != null)
            registry.RegisterPack(definition.SourcePack);

        ContentCatalogResult catalog = ContentCatalog.Load(ContentRoot.Resolve());
        if (catalog.Catalog != null)
        {
            foreach (ContentPackDefinition pack in catalog.Catalog.Packs)
            {
                registry.RegisterPack(pack);
            }
        }

        return registry;
    }

    public void RegisterPack(ContentPackDefinition pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        string root = Path.Combine(pack.DirectoryPath, "textures");
        if (Directory.Exists(root))
            RegisterLibrary(pack.Manifest.Id, root, pack.Manifest.Name);
    }

    public void RegisterLibrary(string id, string rootPath, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string root = Path.GetFullPath(rootPath);
        if (_libraries.TryGetValue(id, out string? existing))
        {
            if (!string.Equals(existing, root, PathComparison()))
                throw new InvalidOperationException($"Texture library '{id}' is already registered at {existing}.");
            return;
        }
        _libraries.Add(id, root);
        _libraryNames.Add(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName);
    }

    public IReadOnlyList<MapEditorTextureCatalogItem> DiscoverTextures()
    {
        List<MapEditorTextureCatalogItem> items = [];
        HashSet<string> includedFiles = new(PathComparer());
        foreach ((string id, string root) in _libraries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (string file in EnumerateTextureFiles(root))
            {
                string relative = Path.GetRelativePath(root, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                MapEditorTextureReference reference = MapEditorTextureReference.Library(id, relative);
                includedFiles.Add(Path.GetFullPath(file));
                items.Add(new MapEditorTextureCatalogItem(reference, _libraryNames[id], relative));
            }
        }

        string projectRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"));
        string textureRoot = Path.GetFullPath(ProjectSettings.GlobalizePath(PROJECT_TEXTURE_ROOT));
        foreach (string file in EnumerateTextureFiles(textureRoot))
        {
            string fullPath = Path.GetFullPath(file);
            if (includedFiles.Contains(fullPath))
                continue;
            string projectRelative = Path.GetRelativePath(projectRoot, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            string name = Path.GetRelativePath(textureRoot, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            MapEditorTextureReference reference = MapEditorTextureReference.Project(projectRelative);
            items.Add(new MapEditorTextureCatalogItem(reference, "Official", name));
        }

        return items.OrderBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public MapEditorTextureLoadResult Load(MapEditorTextureReference reference)
    {
        if (reference.Source == MapEditorTextureReference.PROJECT_SOURCE)
            return _project.Load(reference);
        if (!reference.Source.StartsWith(MapEditorTextureReference.LIBRARY_SOURCE_PREFIX,
                StringComparison.Ordinal))
        {
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                Message: $"Texture source '{reference.Source}' isn't supported.");
        }

        string id = reference.Source[MapEditorTextureReference.LIBRARY_SOURCE_PREFIX.Length..];
        if (!_libraries.TryGetValue(id, out string? root))
        {
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.MISSING,
                Message: $"Texture library '{id}' isn't available.");
        }

        try
        {
            string path = Path.GetFullPath(Path.Combine(root,
                reference.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsDescendantPath(root, path) || ContainsSymlink(root, reference.Path))
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"Texture path leaves the '{id}' library: {reference.Path}");
            }
            if (!File.Exists(path))
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.MISSING);

            using Image image = new();
            Error loadError = image.Load(path);
            if (loadError != Error.Ok || image.IsEmpty())
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"Couldn't open {reference.Location}: {loadError}");
            }
            if (image.IsCompressed() && image.Decompress() != Error.Ok)
            {
                return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                    Message: $"This texture can't be opened: {reference.Location}");
            }
            if (image.GetFormat() != Image.Format.Rgba8)
                image.Convert(Image.Format.Rgba8);
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.RESOLVED,
                new MapEditorTextureData(image.GetWidth(), image.GetHeight(), image.GetData()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                Message: $"Couldn't open {reference.Location}: {exception.Message}");
        }
    }

    private static bool IsDescendantPath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static bool ContainsSymlink(string root, string relativePath)
    {
        string current = root;
        foreach (string segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static IEnumerable<string> EnumerateTextureFiles(string root)
    {
        if (!Directory.Exists(root))
            yield break;
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(directory).ToArray();
                directories = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                if (_textureExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
            foreach (string child in directories)
            {
                if (Path.GetFileName(child) == ".godot" || IsSymlink(child))
                {
                    continue;
                }
                pending.Push(child);
            }
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

public class MapEditorTextureResolver : IMapEditorTextureResolver
{
    private readonly IMapEditorTextureAccess _textures;
    private readonly Dictionary<MapEditorTextureReference, MapEditorTextureResolution> _results = [];

    public MapEditorTextureResolver(IMapEditorTextureAccess? textures = null)
    {
        _textures = textures ?? new MapEditorTextureSourceRegistry();
    }

    public MapEditorTextureResolution Resolve(MapEditorTextureReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_results.TryGetValue(reference, out MapEditorTextureResolution? cached))
            return cached;

        MapEditorTextureResolution result = ResolveUncached(reference);
        _results.Add(reference, result);
        return result;
    }

    public void Invalidate() => _results.Clear();

    private MapEditorTextureResolution ResolveUncached(MapEditorTextureReference reference)
    {
        MapEditorTextureLoadResult load;
        try
        {
            load = _textures.Load(reference);
        }
        catch (Exception exception)
        {
            load = new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.LOAD_ERROR,
                Message: $"Couldn't open {reference.Location}: {exception.Message}");
        }
        if (load.Status == MapEditorTextureLoadStatus.RESOLVED && load.Texture != null)
        {
            return new MapEditorTextureResolution(MapEditorTextureResolutionStatus.RESOLVED,
                reference, load.Texture, $"Texture loaded from {reference.Location}.",
                reference.Location);
        }
        if (load.Status == MapEditorTextureLoadStatus.LOAD_ERROR)
        {
            return new MapEditorTextureResolution(MapEditorTextureResolutionStatus.LOAD_ERROR,
                reference, null, load.Message ?? $"Couldn't open {reference.Location}.");
        }
        return new MapEditorTextureResolution(MapEditorTextureResolutionStatus.MISSING,
            reference, null, load.Message ?? $"Texture not found: {reference.Location}");
    }

    public static string Describe(MapEditorTextureReference reference) => reference.Location;
}

public static class MapEditorMissingTexturePreview
{
    public static MapEditorTextureData Create(int size = 16, int cellSize = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        byte[] pixels = new byte[checked(size * size * 4)];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = (y * size + x) * 4;
                bool magenta = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                pixels[offset] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }
        return new MapEditorTextureData(size, size, pixels);
    }
}
