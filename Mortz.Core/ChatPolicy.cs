using System.Globalization;
using System.Text;
using Mortz.Core.Text;

namespace Mortz.Core;

public enum ChatRejectReason
{
    None,
    Empty,
    TooLong,
    Command,
    RateLimited,
}

public static class ChatTextSanitizer
{
    public static bool TrySanitize(string? value, out string sanitized, out ChatRejectReason reason)
    {
        sanitized = "";
        reason = ChatRejectReason.Empty;
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
                reason = ChatRejectReason.TooLong;
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
            reason = ChatRejectReason.Command;
            return false;
        }
        reason = ChatRejectReason.None;
        return true;
    }
}

public sealed class ChatPolicy
{
    private readonly PeerRateLimiter _limiter = new(capacity: 5, tokensPerSecond: 1);

    public bool TryAccept(long peerId, ulong nowMs, string? value,
        out string sanitized, out ChatRejectReason reason)
    {
        if (!_limiter.Allow(peerId, nowMs))
        {
            sanitized = "";
            reason = ChatRejectReason.RateLimited;
            return false;
        }
        return ChatTextSanitizer.TrySanitize(value, out sanitized, out reason);
    }

    public void Remove(long peerId) => _limiter.Remove(peerId);
    public void Reset() => _limiter.Reset();
}
