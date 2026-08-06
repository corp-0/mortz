using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Mortz.Content;

public static class TomlModel
{
    public static ContentReadResult<T> ReadFile<T>(string path) where T : class
    {
        try
        {
            return Read<T>(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null,
            [
                new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, path, exception.Message),
            ]);
        }
    }

    public static ContentReadResult<T> Read<T>(string text, string source = "input.toml") where T : class
    {
        List<ContentDiagnostic> diagnostics = [];
        DocumentSyntax syntax = Toml.Parse(text, source);
        foreach (DiagnosticMessage diagnostic in syntax.Diagnostics)
        {
            diagnostics.Add(new ContentDiagnostic(
                diagnostic.Kind == DiagnosticMessageKind.Error
                    ? ContentDiagnosticSeverity.ERROR
                    : ContentDiagnosticSeverity.WARNING,
                source, diagnostic.Message));
        }
        if (syntax.HasErrors)
            return new(null, diagnostics);

        if (!TomlGeneratedModels.TryRead(typeof(T), Toml.ToModel(syntax), "", source,
                diagnostics, out object? value))
            throw new NotSupportedException($"'{typeof(T).Name}' is not marked [TomlModel]");
        return new(diagnostics.Any(IsError) ? null : (T?)value, diagnostics);
    }

    public static string Write<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TomlGeneratedModels.TryWrite(value.GetType(), value, out TomlTable? table))
            throw new NotSupportedException($"'{value.GetType().Name}' is not marked [TomlModel]");
        return Toml.FromModel(table!);
    }

    public static void UnknownKeys(TomlTable table, string[] known, string path, string source,
        List<ContentDiagnostic> diagnostics)
    {
        HashSet<string> keys = new(known, StringComparer.Ordinal);
        foreach (string key in table.Keys.Where(key => !keys.Contains(key)).Order(StringComparer.Ordinal))
            Warning(diagnostics, source, $"unknown key '{Child(path, key)}'");
    }

    public static bool Required(TomlTable table, string key, string path, string source,
        List<ContentDiagnostic> diagnostics, out object? raw)
    {
        if (table.TryGetValue(key, out raw))
            return true;
        Error(diagnostics, source, path.Length == 0
            ? $"missing required key '{key}'"
            : $"{path} is missing required key '{key}'");
        return false;
    }

    public static bool Scalar<T>(object? raw, string path, string source,
        List<ContentDiagnostic> diagnostics, out T value)
    {
        object? converted = typeof(T) switch
        {
            Type t when t == typeof(bool) && raw is bool => raw,
            Type t when t == typeof(int) && raw is long n && n is >= int.MinValue and <= int.MaxValue => (int)n,
            Type t when t == typeof(long) && raw is long => raw,
            Type t when t == typeof(float) && raw is double n => (float)n,
            Type t when t == typeof(float) && raw is long n => (float)n,
            Type t when t == typeof(double) && raw is double => raw,
            Type t when t == typeof(double) && raw is long n => (double)n,
            Type t when t == typeof(string) && raw is string => raw,
            Type t when t.IsEnum && raw is string name => ParseEnum(t, name),
            _ => null,
        };
        if (converted != null)
        {
            value = (T)converted;
            return true;
        }
        value = default!;
        Error(diagnostics, source, $"'{path}' must be {Expected(typeof(T))}");
        return false;
    }

    public static object WriteScalar(object value) => value is Enum
        ? Snake(value.ToString()!)
        : value;

    public static string Child(string path, string key) =>
        path.Length == 0 ? key : $"{path}.{key}";

    public static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new(ContentDiagnosticSeverity.ERROR, source, message));

    private static void Warning(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new(ContentDiagnosticSeverity.WARNING, source, message));
    private static bool IsError(ContentDiagnostic diagnostic) =>
        diagnostic.Severity == ContentDiagnosticSeverity.ERROR;
    private static object? ParseEnum(Type type, string name) => Enum.GetValues(type).Cast<object>()
        .FirstOrDefault(value => Snake(value.ToString()!) == name);
    private static string Expected(Type type) => type.IsEnum
        ? "one of: " + string.Join(", ", Enum.GetNames(type).Select(Snake))
        : type == typeof(bool) ? "a boolean"
        : type == typeof(int) ? "a 32-bit integer"
        : type == typeof(long) ? "an integer"
        : type == typeof(float) || type == typeof(double) ? "a number"
        : "a string";

    internal static string Snake(string name)
    {
        System.Text.StringBuilder result = new();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_' && (!char.IsUpper(name[i - 1]) ||
                i + 1 < name.Length && char.IsLower(name[i + 1]))) result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
