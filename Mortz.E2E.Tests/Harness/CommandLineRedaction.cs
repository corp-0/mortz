namespace Mortz.E2E.Tests.Harness;

/// <summary>Command lines are world-readable in process lists and the manifest
/// is archived by CI, so a secret must never reach one.</summary>
public static class CommandLineRedaction
{
    public const string PLACEHOLDER = "[redacted]";

    private static readonly HashSet<string> _secretFlags =
        new(StringComparer.Ordinal) { "--admin-password" };

    public static IReadOnlyList<string> Redact(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string[] redacted = args.ToArray();
        for (int i = 0; i < redacted.Length - 1; i++)
        {
            if (_secretFlags.Contains(redacted[i]))
                redacted[i + 1] = PLACEHOLDER;
        }
        return redacted;
    }

    public static string Format(string fileName, IReadOnlyList<string> args) =>
        string.Join(' ', new[] { fileName }.Concat(Redact(args)).Select(Quote));

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
