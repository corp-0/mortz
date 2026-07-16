using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Mortz.Content;

public sealed record ContentPackManifest(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    int LoadOrder);

public readonly record struct MapSpawnPoint(int X, int Y);

public sealed record MapManifest(
    int FormatVersion,
    string Name,
    int SuggestedPlayers,
    ImmutableArray<MapSpawnPoint> SpawnPoints)
{
    public const int CURRENT_FORMAT_VERSION = 1;

    public MapManifest(int formatVersion, string name, int suggestedPlayers)
        : this(formatVersion, name, suggestedPlayers, [])
    {
    }
}

public static partial class ContentManifestReader
{
    private static readonly HashSet<string> _packKeys =
    [
        "id", "name", "version", "author", "description", "load_order",
    ];

    private static readonly HashSet<string> _mapKeys =
    [
        "format_version", "name", "suggested_players", "spawn_points",
    ];

    public static ContentReadResult<ContentPackManifest> ReadPackFile(string path) =>
        ReadFile(path, ReadPack);

    public static ContentReadResult<MapManifest> ReadMapFile(string path) =>
        ReadFile(path, ReadMap);

    public static ContentReadResult<ContentPackManifest> ReadPack(string text, string source = "content_pack.toml")
    {
        List<ContentDiagnostic> diagnostics = [];
        TomlTable? table = Parse(text, source, diagnostics);
        if (table == null)
            return new ContentReadResult<ContentPackManifest>(null, diagnostics);

        WarnUnknownKeys(table, _packKeys, source, diagnostics);
        string? id = RequiredString(table, "id", source, diagnostics);
        string? name = RequiredString(table, "name", source, diagnostics);
        string? version = RequiredString(table, "version", source, diagnostics);
        string author = OptionalString(table, "author", source, diagnostics) ?? "";
        string description = OptionalString(table, "description", source, diagnostics) ?? "";
        int loadOrder = OptionalInt(table, "load_order", 0, source, diagnostics);

        if (id != null && !LogicalIdPattern().IsMatch(id))
            Error(diagnostics, source, "'id' must contain only lowercase letters, digits, '_' or '-', and begin with a letter or digit");

        ContentPackManifest? manifest = diagnostics.Any(IsError) || id == null || name == null || version == null
            ? null
            : new ContentPackManifest(id, name, version, author, description, loadOrder);
        return new ContentReadResult<ContentPackManifest>(manifest, diagnostics);
    }

    public static ContentReadResult<MapManifest> ReadMap(string text, string source = "map.toml")
    {
        List<ContentDiagnostic> diagnostics = [];
        TomlTable? table = Parse(text, source, diagnostics);
        if (table == null)
            return new ContentReadResult<MapManifest>(null, diagnostics);

        WarnUnknownKeys(table, _mapKeys, source, diagnostics);
        int? formatVersion = RequiredInt(table, "format_version", source, diagnostics);
        string? name = RequiredString(table, "name", source, diagnostics);
        int? suggestedPlayers = RequiredInt(table, "suggested_players", source, diagnostics);
        ImmutableArray<MapSpawnPoint> spawnPoints = ReadSpawnPoints(table, source, diagnostics);

        if (formatVersion is not null && formatVersion != MapManifest.CURRENT_FORMAT_VERSION)
            Error(diagnostics, source, $"unsupported format_version {formatVersion}; expected {MapManifest.CURRENT_FORMAT_VERSION}");
        if (suggestedPlayers is not null && suggestedPlayers <= 0)
            Error(diagnostics, source, "'suggested_players' must be greater than zero");

        MapManifest? manifest = diagnostics.Any(IsError) || formatVersion == null || name == null || suggestedPlayers == null
            ? null
            : new MapManifest(formatVersion.Value, name, suggestedPlayers.Value, spawnPoints);
        return new ContentReadResult<MapManifest>(manifest, diagnostics);
    }

    public static string WriteMap(MapManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != MapManifest.CURRENT_FORMAT_VERSION)
            throw new ArgumentOutOfRangeException(nameof(manifest), "unsupported map format version");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new ArgumentException("map name is required", nameof(manifest));
        if (manifest.SuggestedPlayers <= 0)
            throw new ArgumentOutOfRangeException(nameof(manifest), "suggested players must be greater than zero");
        if (manifest.SpawnPoints.Distinct().Count() != manifest.SpawnPoints.Length)
            throw new ArgumentException("spawn points must be distinct", nameof(manifest));

        StringBuilder result = new();
        result.Append(CultureInfo.InvariantCulture,
            $"format_version = {manifest.FormatVersion}\n");
        result.Append("name = \"").Append(EscapeBasicString(manifest.Name)).Append("\"\n");
        result.Append(CultureInfo.InvariantCulture,
            $"suggested_players = {manifest.SuggestedPlayers}\n");
        foreach (MapSpawnPoint point in manifest.SpawnPoints)
        {
            result.Append("\n[[spawn_points]]\n");
            result.Append(CultureInfo.InvariantCulture, $"x = {point.X}\n");
            result.Append(CultureInfo.InvariantCulture, $"y = {point.Y}\n");
        }
        return result.ToString();
    }

    public static bool IsLogicalId(string value) => LogicalIdPattern().IsMatch(value);

    private static ContentReadResult<T> ReadFile<T>(string path,
        Func<string, string, ContentReadResult<T>> reader) where T : class
    {
        try
        {
            return reader(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ContentReadResult<T>(null,
            [
                new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, path, exception.Message),
            ]);
        }
    }

    private static TomlTable? Parse(string text, string source, List<ContentDiagnostic> diagnostics)
    {
        DocumentSyntax syntax = Toml.Parse(text, source);
        foreach (DiagnosticMessage diagnostic in syntax.Diagnostics)
        {
            diagnostics.Add(new ContentDiagnostic(
                diagnostic.Kind == DiagnosticMessageKind.Error
                    ? ContentDiagnosticSeverity.ERROR
                    : ContentDiagnosticSeverity.WARNING,
                source,
                diagnostic.Message));
        }
        return syntax.HasErrors ? null : Toml.ToModel(syntax);
    }

    private static void WarnUnknownKeys(TomlTable table, HashSet<string> known, string source,
        List<ContentDiagnostic> diagnostics)
    {
        foreach (string key in table.Keys.Where(key => !known.Contains(key)).Order(StringComparer.Ordinal))
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.WARNING, source,
                $"unknown key '{key}'"));
        }
    }

    private static string? RequiredString(TomlTable table, string key, string source,
        List<ContentDiagnostic> diagnostics)
    {
        string? value = OptionalString(table, key, source, diagnostics);
        if (!table.ContainsKey(key))
            Error(diagnostics, source, $"missing required key '{key}'");
        else if (value != null && string.IsNullOrWhiteSpace(value))
        {
            Error(diagnostics, source, $"'{key}' must not be empty");
            value = null;
        }
        return value;
    }

    private static string? OptionalString(TomlTable table, string key, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value))
            return null;
        if (value is string text)
            return text;
        Error(diagnostics, source, $"'{key}' must be a string");
        return null;
    }

    private static int? RequiredInt(TomlTable table, string key, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value))
        {
            Error(diagnostics, source, $"missing required key '{key}'");
            return null;
        }
        return ConvertInt(value, key, source, diagnostics);
    }

    private static ImmutableArray<MapSpawnPoint> ReadSpawnPoints(TomlTable table, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue("spawn_points", out object? value))
            return [];
        if (value is not TomlTableArray entries)
        {
            Error(diagnostics, source, "'spawn_points' must be an array of tables");
            return [];
        }

        ImmutableArray<MapSpawnPoint>.Builder points = ImmutableArray.CreateBuilder<MapSpawnPoint>(entries.Count);
        Dictionary<MapSpawnPoint, int> firstIndexByPoint = [];
        for (int i = 0; i < entries.Count; i++)
        {
            TomlTable entry = entries[i];
            foreach (string key in entry.Keys.Where(key => key is not "x" and not "y")
                         .Order(StringComparer.Ordinal))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.WARNING, source,
                    $"unknown key 'spawn_points[{i}].{key}'"));
            }

            int? x = RequiredSpawnInt(entry, "x", i, source, diagnostics);
            int? y = RequiredSpawnInt(entry, "y", i, source, diagnostics);
            if (x is not null && y is not null)
            {
                MapSpawnPoint point = new(x.Value, y.Value);
                if (firstIndexByPoint.TryGetValue(point, out int firstIndex))
                    Error(diagnostics, source,
                        $"spawn_points[{i}] duplicates spawn_points[{firstIndex}]");
                else
                {
                    firstIndexByPoint.Add(point, i);
                    points.Add(point);
                }
            }
        }
        return points.ToImmutable();
    }

    private static int? RequiredSpawnInt(TomlTable table, string key, int index, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value))
        {
            Error(diagnostics, source, $"spawn_points[{index}] is missing required key '{key}'");
            return null;
        }
        if (value is long integer && integer is >= int.MinValue and <= int.MaxValue)
            return (int)integer;
        Error(diagnostics, source, $"'spawn_points[{index}].{key}' must be a 32-bit integer");
        return null;
    }

    private static int OptionalInt(TomlTable table, string key, int fallback, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value))
            return fallback;
        return ConvertInt(value, key, source, diagnostics) ?? fallback;
    }

    private static int? ConvertInt(object? value, string key, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (value is long integer && integer is >= int.MinValue and <= int.MaxValue)
            return (int)integer;
        Error(diagnostics, source, $"'{key}' must be a 32-bit integer");
        return null;
    }

    private static string EscapeBasicString(string value)
    {
        StringBuilder result = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    throw new ArgumentException("map name contains an unpaired UTF-16 surrogate", nameof(value));
                result.Append(c).Append(value[++i]);
                continue;
            }
            if (char.IsLowSurrogate(c))
                throw new ArgumentException("map name contains an unpaired UTF-16 surrogate", nameof(value));
            result.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }
        return result.ToString();
    }

    private static bool IsError(ContentDiagnostic diagnostic) =>
        diagnostic.Severity == ContentDiagnosticSeverity.ERROR;

    private static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message));

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalIdPattern();
}
