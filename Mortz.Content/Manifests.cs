using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Mortz.Core.Match;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Mortz.Content;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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

public sealed record GameModeManifest(
    int FormatVersion,
    string Name,
    string Description,
    MatchConfig Config,
    ModeIdentity? Identity,
    ImmutableArray<AuthoredKey> PhysicsOverrides)
{
    public const int CURRENT_FORMAT_VERSION = 1;

    public int IdentitySpecificity => Identity?.KeyCount ?? int.MaxValue;

    public bool MatchesIdentity(MatchConfig current)
    {
        if (Identity == null)
        {
            return current.Rules.ToBytes().AsSpan().SequenceEqual(Config.Rules.ToBytes()) &&
                   Matches(current.Physics, PhysicsOverrides);
        }
        return Matches(current.Rules, Identity.Rules) &&
               Matches(current.Physics, Identity.Physics);
    }

    private static bool Matches(ModeRules current, ImmutableArray<AuthoredKey> expected)
    {
        byte[] currentBytes = current.ToBytes();
        ModeRules overlaid = ModeRules.FromBytes(currentBytes);
        foreach (AuthoredKey authored in expected)
            overlaid.TryApplyKey(authored.Key, authored.Value, out _);
        overlaid.Clamp();
        return currentBytes.AsSpan().SequenceEqual(overlaid.ToBytes());
    }

    private static bool Matches(Physics current, ImmutableArray<AuthoredKey> expected)
    {
        byte[] currentBytes = current.ToBytes();
        Physics overlaid = Physics.FromBytes(currentBytes);
        foreach (AuthoredKey authored in expected)
            overlaid.TryApplyKey(authored.Key, authored.Value, out _);
        overlaid.Clamp();
        return currentBytes.AsSpan().SequenceEqual(overlaid.ToBytes());
    }
}

public sealed record ModeIdentity(
    ImmutableArray<AuthoredKey> Rules,
    ImmutableArray<AuthoredKey> Physics)
{
    public int KeyCount => Rules.Length + Physics.Length;
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

    private static readonly HashSet<string> _modeKeys =
    [
        "format_version", "name", "description", "identity", "rules", "physics",
    ];

    private static readonly HashSet<string> _rulesetKeys =
    [
        "rules", "physics",
    ];

    public static ContentReadResult<ContentPackManifest> ReadPackFile(string path) =>
        ReadFile(path, ReadPack);

    public static ContentReadResult<MapManifest> ReadMapFile(string path) =>
        ReadFile(path, ReadMap);

    public static ContentReadResult<GameModeManifest> ReadModeFile(string path) =>
        ReadFile(path, ReadMode);

    public static ContentReadResult<MatchConfig> ReadRulesetFile(string path) =>
        ReadFile(path, ReadRuleset);

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

    public static ContentReadResult<GameModeManifest> ReadMode(string text, string source = "mode.toml")
    {
        List<ContentDiagnostic> diagnostics = [];
        TomlTable? table = Parse(text, source, diagnostics);
        if (table == null)
            return new ContentReadResult<GameModeManifest>(null, diagnostics);

        WarnUnknownKeys(table, _modeKeys, source, diagnostics);
        int? formatVersion = RequiredInt(table, "format_version", source, diagnostics);
        string? name = RequiredString(table, "name", source, diagnostics);
        string description = OptionalString(table, "description", source, diagnostics) ?? "";
        ModeRules rules = ReadRulesTable(table, source, diagnostics,
            out ImmutableArray<AuthoredKey> authoredRules);
        Physics physics = ReadPhysicsTable(table, source, diagnostics,
            out ImmutableArray<AuthoredKey> authoredPhysics);
        ModeIdentity? identity = ReadModeIdentity(
            table, authoredRules, authoredPhysics, source, diagnostics);

        if (formatVersion is not null && formatVersion != GameModeManifest.CURRENT_FORMAT_VERSION)
            Error(diagnostics, source, $"unsupported format_version {formatVersion}; expected {GameModeManifest.CURRENT_FORMAT_VERSION}");

        GameModeManifest? manifest = diagnostics.Any(IsError) || formatVersion == null || name == null
            ? null
            : new GameModeManifest(formatVersion.Value, name, description,
                new MatchConfig { Rules = rules, Physics = physics },
                identity, authoredPhysics);
        return new ContentReadResult<GameModeManifest>(manifest, diagnostics);
    }

    public static ContentReadResult<MatchConfig> ReadRuleset(string text, string source = "ruleset.toml")
    {
        List<ContentDiagnostic> diagnostics = [];
        TomlTable? table = Parse(text, source, diagnostics);
        if (table == null)
            return new ContentReadResult<MatchConfig>(null, diagnostics);

        WarnUnknownKeys(table, _rulesetKeys, source, diagnostics);
        ModeRules rules = ReadRulesTable(table, source, diagnostics, out _);
        Physics physics = ReadPhysicsTable(table, source, diagnostics, out _);
        MatchConfig config = new() { Rules = rules, Physics = physics };
        return new ContentReadResult<MatchConfig>(diagnostics.Any(IsError) ? null : config, diagnostics);
    }

    private delegate ConfigKeyResult TryApplyKey(string key, object? value, out string error);

    /// <summary>A missing [rules] table is legal and means all defaults.</summary>
    private static ModeRules ReadRulesTable(TomlTable table, string source,
        List<ContentDiagnostic> diagnostics, out ImmutableArray<AuthoredKey> authored)
    {
        ModeRules rules = new();
        authored = ApplySection(table, "rules", rules.TryApplyKey, source, diagnostics);
        rules.Clamp();
        return rules;
    }

    private static ModeIdentity? ReadModeIdentity(
        TomlTable table,
        ImmutableArray<AuthoredKey> authoredRules,
        ImmutableArray<AuthoredKey> authoredPhysics,
        string source,
        List<ContentDiagnostic> diagnostics)
    {
        const string RULES_PREFIX = "rules.";
        const string PHYSICS_PREFIX = "physics.";

        if (!table.TryGetValue("identity", out object value))
            return null;
        if (value is not TomlArray paths)
        {
            Error(diagnostics, source, "'identity' must be an array of strings");
            return null;
        }
        if (paths.Count == 0)
        {
            Error(diagnostics, source, "'identity' must contain at least one key");
            return null;
        }

        Dictionary<string, AuthoredKey> rules = authoredRules
            .ToDictionary(authored => authored.Key, StringComparer.Ordinal);
        Dictionary<string, AuthoredKey> physics = authoredPhysics
            .ToDictionary(authored => authored.Key, StringComparer.Ordinal);
        ImmutableArray<AuthoredKey>.Builder identityRules =
            ImmutableArray.CreateBuilder<AuthoredKey>();
        ImmutableArray<AuthoredKey>.Builder identityPhysics =
            ImmutableArray.CreateBuilder<AuthoredKey>();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (object? item in paths)
        {
            if (item is not string path)
            {
                Error(diagnostics, source, "'identity' must contain only strings");
                continue;
            }
            if (!seen.Add(path))
            {
                Error(diagnostics, source, $"'identity' contains duplicate key '{path}'");
                continue;
            }

            if (path.StartsWith(RULES_PREFIX, StringComparison.Ordinal) &&
                rules.TryGetValue(path[RULES_PREFIX.Length..], out AuthoredKey rule))
            {
                identityRules.Add(rule);
                continue;
            }
            if (path.StartsWith(PHYSICS_PREFIX, StringComparison.Ordinal) &&
                physics.TryGetValue(path[PHYSICS_PREFIX.Length..], out AuthoredKey property))
            {
                identityPhysics.Add(property);
                continue;
            }
            Error(diagnostics, source,
                $"identity key '{path}' must name a value authored under [rules] or [physics]");
        }

        return new ModeIdentity(identityRules.ToImmutable(), identityPhysics.ToImmutable());
    }

    private static Physics ReadPhysicsTable(TomlTable table, string source,
        List<ContentDiagnostic> diagnostics, out ImmutableArray<AuthoredKey> authored)
    {
        Physics physics = new();
        authored = ApplySection(table, "physics", physics.TryApplyKey, source, diagnostics);
        physics.Clamp();
        return physics;
    }

    private static ImmutableArray<AuthoredKey> ApplySection(TomlTable table, string name,
        TryApplyKey apply, string source, List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(name, out object value))
            return [];
        if (value is not TomlTable section)
        {
            Error(diagnostics, source, $"'{name}' must be a table");
            return [];
        }

        ImmutableArray<AuthoredKey>.Builder applied = ImmutableArray.CreateBuilder<AuthoredKey>();
        foreach (string key in section.Keys)
        {
            switch (apply(key, section[key], out string error))
            {
                case ConfigKeyResult.APPLIED:
                    applied.Add(new AuthoredKey(key, section[key]));
                    break;
                case ConfigKeyResult.UNKNOWN_KEY:
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.WARNING, source,
                        $"unknown key '{name}.{key}'"));
                    break;
                case ConfigKeyResult.INVALID_VALUE:
                    Error(diagnostics, source, $"{name}.{key}: {error}");
                    break;
            }
        }
        return applied.ToImmutable();
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
        if (!table.TryGetValue(key, out object value))
            return null;
        if (value is string text)
            return text;
        Error(diagnostics, source, $"'{key}' must be a string");
        return null;
    }

    private static int? RequiredInt(TomlTable table, string key, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object value))
        {
            Error(diagnostics, source, $"missing required key '{key}'");
            return null;
        }
        return ConvertInt(value, key, source, diagnostics);
    }

    private static ImmutableArray<MapSpawnPoint> ReadSpawnPoints(TomlTable table, string source,
        List<ContentDiagnostic> diagnostics)
    {
        if (!table.TryGetValue("spawn_points", out object value))
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
        if (!table.TryGetValue(key, out object value))
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
        if (!table.TryGetValue(key, out object value))
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
