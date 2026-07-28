namespace Mortz.Core.Net.Query;

/// <summary>Token buckets per source address, plus a global ceiling. There is
/// no connection to remove a key on, so idle buckets are evicted or the
/// dictionary is itself the DoS. Callers supply monotonic milliseconds.</summary>
public sealed class ServerQueryRateLimiter(
    double perSourcePerSecond = 4,
    double globalPerSecond = 40,
    int maxSources = 512)
{
    private const double BURST_SECONDS = 2;
    private const ulong IDLE_EVICTION_MS = 30_000;

    private sealed class Bucket(double tokens, ulong updatedAtMs)
    {
        public double Tokens = tokens;
        public ulong UpdatedAtMs = updatedAtMs;
    }

    private readonly Dictionary<string, Bucket> _sources = new();
    private readonly Bucket _global = new(globalPerSecond * BURST_SECONDS, 0);

    // Peek both buckets, then commit both, so a denial never costs tokens.
    // Checking the global pool first without spending it means a spoofed
    // flood cannot allocate buckets once the pool is dry.
    public bool Allow(string source, ulong nowMs)
    {
        Refill(_global, globalPerSecond, nowMs);
        if (_global.Tokens < 1)
            return false;
        if (!_sources.TryGetValue(source, out Bucket? bucket))
        {
            if (_sources.Count >= maxSources)
                EvictIdle(nowMs);
            if (_sources.Count >= maxSources)
                EvictLeastRecent();
            bucket = new Bucket(perSourcePerSecond * BURST_SECONDS, nowMs);
            _sources.Add(source, bucket);
        }
        Refill(bucket, perSourcePerSecond, nowMs);
        if (bucket.Tokens < 1)
            return false;
        _global.Tokens -= 1;
        bucket.Tokens -= 1;
        return true;
    }

    private static void Refill(Bucket bucket, double perSecond, ulong nowMs)
    {
        if (nowMs <= bucket.UpdatedAtMs)
            return;
        bucket.Tokens = Math.Min(perSecond * BURST_SECONDS,
            bucket.Tokens + (nowMs - bucket.UpdatedAtMs) * perSecond / 1000d);
        bucket.UpdatedAtMs = nowMs;
    }

    private void EvictIdle(ulong nowMs)
    {
        foreach (string source in _sources
                     .Where(entry => nowMs - entry.Value.UpdatedAtMs > IDLE_EVICTION_MS)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            _sources.Remove(source);
        }
    }

    // Full table: drop the oldest rather than refuse the new source, or a
    // spoofed flood locks every real browser out until it stops.
    private void EvictLeastRecent()
    {
        string? oldest = null;
        ulong oldestSeen = ulong.MaxValue;
        foreach ((string source, Bucket bucket) in _sources)
        {
            if (bucket.UpdatedAtMs >= oldestSeen)
                continue;
            oldest = source;
            oldestSeen = bucket.UpdatedAtMs;
        }
        if (oldest != null)
            _sources.Remove(oldest);
    }
}
