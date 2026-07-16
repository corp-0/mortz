namespace Mortz.Core.Chat;

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
