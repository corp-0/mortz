namespace Mortz.Core.Net.Abuse;

/// <summary>Deterministic token bucket for one subject. Callers supply monotonic milliseconds.
/// A new bucket starts full.</summary>
public struct RateBucket
{
    private readonly double _capacity;
    private readonly double _tokensPerMs;
    private double _tokens;
    private ulong _updatedAtMs;

    public RateBucket(double capacity, double tokensPerSecond)
    {
        if (!double.IsFinite(capacity) || capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!double.IsFinite(tokensPerSecond) || tokensPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        _capacity = capacity;
        _tokensPerMs = tokensPerSecond / 1000d;
        _tokens = capacity;
        _updatedAtMs = 0;
    }

    public bool Allow(ulong nowMs, double cost = 1)
    {
        if (!double.IsFinite(cost) || cost <= 0 || cost > _capacity)
            return false;
        if (nowMs > _updatedAtMs)
        {
            _tokens = Math.Min(_capacity, _tokens + (nowMs - _updatedAtMs) * _tokensPerMs);
            _updatedAtMs = nowMs;
        }

        if (_tokens + 1e-9 < cost)
            return false;
        _tokens -= cost;
        return true;
    }
}
