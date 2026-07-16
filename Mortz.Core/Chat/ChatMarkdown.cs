using System.Text;
using Mortz.Core.Text;

namespace Mortz.Core.Chat;

/// <summary>
/// Safe inline Markdown accepted from chat users and rendered as Godot BBCode.
/// Supports bold, italic, strikethrough, inline code, and HTTP(S) links.
/// </summary>
public static class ChatMarkdown
{
    private const int MAX_NESTING = 16;

    /// <summary>
    /// Removes BBCode-looking tags while preserving their contents. Markdown links are
    /// retained for the renderer.
    /// </summary>
    public static string StripBbCode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var result = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length && text[index + 1] == '[' &&
                TryReadBbCodeTag(text, index + 1, out int escapedTagEnd))
            {
                index = escapedTagEnd;
                continue;
            }
            if (text[index] == '[' && TryReadBbCodeTag(text, index, out int tagEnd))
            {
                index = tagEnd;
                continue;
            }
            result.Append(text[index]);
            index++;
        }
        return result.ToString();
    }

    public static RichText Render(string? markdown)
    {
        string safeMarkdown = StripBbCode(markdown);
        return RichText.FromTrustedBbCode(RenderSegment(safeMarkdown, 0));
    }

    private static string RenderSegment(string source, int depth)
    {
        if (source.Length == 0)
            return "";
        if (depth >= MAX_NESTING)
            return RichText.Escape(source);

        var result = new StringBuilder(source.Length);
        int index = 0;
        while (index < source.Length)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                result.Append(RichText.Escape(source[index + 1].ToString()));
                index += 2;
                continue;
            }

            if (source[index] == '`' &&
                TryDelimited(source, index, "`", out int codeEnd, out string? code))
            {
                result.Append("[code]").Append(RichText.Escape(code)).Append("[/code]");
                index = codeEnd;
                continue;
            }

            if (source[index] == '[' && TryLink(source, index, depth,
                    out int linkEnd, out string? link))
            {
                result.Append(link);
                index = linkEnd;
                continue;
            }

            if (TryStyled(source, index, "**", "b", depth, out int strongEnd,
                    out string? strong) ||
                TryStyled(source, index, "__", "b", depth, out strongEnd, out strong))
            {
                result.Append(strong);
                index = strongEnd;
                continue;
            }

            if (TryStyled(source, index, "~~", "s", depth, out int strikeEnd,
                    out string? strike))
            {
                result.Append(strike);
                index = strikeEnd;
                continue;
            }

            if (TryStyled(source, index, "*", "i", depth, out int italicEnd,
                    out string? italic) ||
                TryStyled(source, index, "_", "i", depth, out italicEnd, out italic))
            {
                result.Append(italic);
                index = italicEnd;
                continue;
            }

            result.Append(RichText.Escape(source[index].ToString()));
            index++;
        }
        return result.ToString();
    }

    private static bool TryStyled(string source, int start, string delimiter, string tag,
        int depth, out int end, out string? rendered)
    {
        end = start;
        rendered = null;
        if (!source.AsSpan(start).StartsWith(delimiter))
            return false;
        int contentStart = start + delimiter.Length;
        int closing = FindUnescaped(source, delimiter, contentStart);
        if (closing <= contentStart || string.IsNullOrWhiteSpace(source[contentStart..closing]))
            return false;
        string content = RenderSegment(source[contentStart..closing], depth + 1);
        rendered = $"[{tag}]{content}[/{tag}]";
        end = closing + delimiter.Length;
        return true;
    }

    private static bool TryDelimited(string source, int start, string delimiter,
        out int end, out string? content)
    {
        end = start;
        content = null;
        int contentStart = start + delimiter.Length;
        int closing = FindUnescaped(source, delimiter, contentStart);
        if (closing <= contentStart)
            return false;
        content = source[contentStart..closing];
        end = closing + delimiter.Length;
        return true;
    }

    private static bool TryLink(string source, int start, int depth,
        out int end, out string? rendered)
    {
        end = start;
        rendered = null;
        int labelEnd = FindUnescaped(source, "](", start + 1);
        if (labelEnd <= start + 1)
            return false;
        int destinationStart = labelEnd + 2;
        int destinationEnd = FindUnescaped(source, ")", destinationStart);
        if (destinationEnd <= destinationStart)
            return false;
        string destination = source[destinationStart..destinationEnd];
        if (!IsSafeUrl(destination))
            return false;
        string label = RenderSegment(source[(start + 1)..labelEnd], depth + 1);
        rendered = $"[url={destination}]{label}[/url]";
        end = destinationEnd + 1;
        return true;
    }

    private static bool IsSafeUrl(string value)
    {
        if (value.Any(character => char.IsWhiteSpace(character) || character is '[' or ']'))
            return false;
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https";
    }

    private static int FindUnescaped(string source, string value, int start)
    {
        int index = start;
        while (index <= source.Length - value.Length)
        {
            int found = source.IndexOf(value, index, StringComparison.Ordinal);
            if (found < 0)
                return -1;
            int slashes = 0;
            for (int before = found - 1; before >= 0 && source[before] == '\\'; before--)
                slashes++;
            if (slashes % 2 == 0)
                return found;
            index = found + value.Length;
        }
        return -1;
    }

    private static bool TryReadBbCodeTag(string text, int start, out int end)
    {
        end = start;
        int close = text.IndexOf(']', start + 1);
        if (close < 0)
            return false;

        // [label](url) is Markdown, not a user-authored BBCode tag.
        if (close + 1 < text.Length && text[close + 1] == '(')
            return false;

        ReadOnlySpan<char> content = text.AsSpan(start + 1, close - start - 1).Trim();
        if (content.IsEmpty)
            return false;
        if (content[0] == '/')
            content = content[1..].TrimStart();
        if (content.IsEmpty || !char.IsAsciiLetter(content[0]))
            return false;

        int nameLength = 1;
        while (nameLength < content.Length &&
            (char.IsAsciiLetterOrDigit(content[nameLength]) || content[nameLength] == '_'))
        {
            nameLength++;
        }
        if (nameLength < content.Length &&
            content[nameLength] is not ('=' or ' ' or '\t'))
        {
            return false;
        }
        end = close + 1;
        return true;
    }
}
