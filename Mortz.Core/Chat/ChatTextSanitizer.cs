using System.Globalization;
using System.Text;
using Mortz.Core.Net;

namespace Mortz.Core.Chat;

public static class ChatTextSanitizer
{
    public static bool TrySanitize(string? value, out string sanitized, out ChatRejectReason reason)
    {
        sanitized = "";
        reason = ChatRejectReason.EMPTY;
        string withoutBbCode = ChatMarkdown.StripBbCode(value);
        if (string.IsNullOrWhiteSpace(withoutBbCode))
            return false;

        var result = new StringBuilder(Math.Min(withoutBbCode.Length, NetConfig.MAX_CHAT_BYTES));
        int utf8Bytes = 0;
        foreach (Rune rune in withoutBbCode.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                continue;
            utf8Bytes += rune.Utf8SequenceLength;
            if (utf8Bytes > NetConfig.MAX_CHAT_BYTES)
            {
                reason = ChatRejectReason.TOO_LONG;
                return false;
            }
            result.Append(rune);
        }

        sanitized = result.ToString().Trim();
        if (sanitized.Length == 0)
            return false;
        if (sanitized[0] == '/')
        {
            sanitized = "";
            reason = ChatRejectReason.COMMAND;
            return false;
        }
        reason = ChatRejectReason.NONE;
        return true;
    }
}
