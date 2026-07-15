namespace Mortz.Content;

public enum ContentDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record ContentDiagnostic(
    ContentDiagnosticSeverity Severity,
    string Source,
    string Message)
{
    public override string ToString() => $"{Source}: {Severity.ToString().ToLowerInvariant()}: {Message}";
}

public sealed class ContentReadResult<T> where T : class
{
    public ContentReadResult(T? value, IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        Value = value;
        Diagnostics = diagnostics;
    }

    public T? Value { get; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error);
}
