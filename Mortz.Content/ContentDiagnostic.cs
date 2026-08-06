namespace Mortz.Content;

public enum ContentDiagnosticSeverity
{
    WARNING,
    ERROR,
}

public sealed record ContentDiagnostic(
    ContentDiagnosticSeverity Severity,
    string Source,
    string Message)
{
    public override string ToString() => $"{Source}: {Severity.ToString().ToLowerInvariant()}: {Message}";
}

public sealed class ContentReadResult<T>(T? value, IReadOnlyList<ContentDiagnostic> diagnostics)
    where T : class
{
    public T? Value { get; } = value;
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; } = diagnostics;
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.ERROR);
}
