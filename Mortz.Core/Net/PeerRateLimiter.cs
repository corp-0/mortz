namespace Mortz.Core.Net;

/// <summary>Deterministic per-peer token buckets. Callers supply monotonic milliseconds.</summary>
public sealed class PeerRateLimiter
{
    private sealed class Bucket(double tokens, ulong updatedAtMs)
    {
        public double Tokens = tokens;
        public ulong UpdatedAtMs = updatedAtMs;
    }

    private readonly double _capacity;
    private readonly double _tokensPerMs;
    private readonly Dictionary<long, Bucket> _buckets = new();

    public PeerRateLimiter(double capacity, double tokensPerSecond)
    {
        if (!double.IsFinite(capacity) || capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!double.IsFinite(tokensPerSecond) || tokensPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        _capacity = capacity;
        _tokensPerMs = tokensPerSecond / 1000d;
    }

    public bool Allow(long peerId, ulong nowMs, double cost = 1)
    {
        if (!double.IsFinite(cost) || cost <= 0 || cost > _capacity)
            return false;
        if (!_buckets.TryGetValue(peerId, out Bucket? bucket))
        {
            bucket = new Bucket(_capacity, nowMs);
            _buckets.Add(peerId, bucket);
        }
        else if (nowMs > bucket.UpdatedAtMs)
        {
            bucket.Tokens = Math.Min(_capacity,
                bucket.Tokens + (nowMs - bucket.UpdatedAtMs) * _tokensPerMs);
            bucket.UpdatedAtMs = nowMs;
        }

        if (bucket.Tokens + 1e-9 < cost)
            return false;
        bucket.Tokens -= cost;
        return true;
    }

    public void Remove(long peerId) => _buckets.Remove(peerId);
    public void Reset() => _buckets.Clear();
}
