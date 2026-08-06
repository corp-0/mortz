using Tomlyn.Model;

namespace Mortz.Content;

public static class TomlGeneratedModels
{
    public delegate object? Reader(TomlTable table, string path, string source,
        List<ContentDiagnostic> diagnostics);
    public delegate TomlTable Writer(object value);

    private static readonly Dictionary<Type, Reader> _readers = [];
    private static readonly Dictionary<Type, Writer> _writers = [];

    public static void Register(Type type, Reader reader, Writer writer)
    {
        _readers[type] = reader;
        _writers[type] = writer;
    }

    internal static bool TryRead(Type type, TomlTable table, string path, string source,
        List<ContentDiagnostic> diagnostics, out object? value)
    {
        if (_readers.TryGetValue(type, out Reader? reader))
        {
            value = reader(table, path, source, diagnostics);
            return true;
        }
        value = null;
        return false;
    }

    internal static bool TryWrite(Type type, object value, out TomlTable? table)
    {
        if (_writers.TryGetValue(type, out Writer? writer))
        {
            table = writer(value);
            return true;
        }
        table = null;
        return false;
    }
}
