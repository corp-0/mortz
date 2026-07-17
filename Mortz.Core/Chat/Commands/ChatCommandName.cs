namespace Mortz.Core.Chat.Commands;

/// <summary>Validated, case-insensitive command name. Raw strings stop at this boundary.</summary>
public readonly record struct ChatCommandName
{
    public ChatCommandName(string value)
    {
        if (!TryNormalize(value, out string normalized))
            throw new ArgumentException("Command names may contain only ASCII letters, digits, '-' and '_'.",
                nameof(value));
        Value = normalized;
    }

    public string Value { get; }
    public override string ToString() => Value;

    internal static bool TryCreate(string value, out ChatCommandName name)
    {
        if (!TryNormalize(value, out string normalized))
        {
            name = default;
            return false;
        }
        name = new ChatCommandName(normalized);
        return true;
    }

    private static bool TryNormalize(string value, out string normalized)
    {
        normalized = value.ToLowerInvariant();
        return normalized.Length > 0 &&
               normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }
}
