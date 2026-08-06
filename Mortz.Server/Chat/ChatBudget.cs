using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Net.Abuse;

namespace Mortz.Server.Chat;

/// <summary>One player's chat rate budget and sanitation. Server lifetime, so
/// the bucket dies with the player and nothing removes anything.</summary>
public sealed class ChatBudget
{
    private RateBucket _bucket = new(capacity: 5, tokensPerSecond: 1);

    public bool TryAccept(ulong nowMs, string? value, out string sanitized,
        out ChatRejectReason reason)
    {
        if (!_bucket.Allow(nowMs))
        {
            sanitized = "";
            reason = ChatRejectReason.RATE_LIMITED;
            return false;
        }

        return ChatTextSanitizer.TrySanitize(value, out sanitized, out reason);
    }

    /// <summary>A roll is a chat line, so it spends the same rate budget.</summary>
    public bool TryAcceptRoll(ulong nowMs) => _bucket.Allow(nowMs);
}
