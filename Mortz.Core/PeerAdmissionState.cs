namespace Mortz.Core;

/// <summary>Pure one-shot Hello state and deadline tracking for server peers.</summary>
public sealed class PeerAdmissionState
{
    private readonly ulong _timeoutMs;
    private readonly Dictionary<long, ulong> _pending = new();
    private readonly HashSet<long> _validated = new();

    public PeerAdmissionState(ulong timeoutMs = NetConfig.HELLO_TIMEOUT_MS) => _timeoutMs = timeoutMs;

    public IReadOnlyCollection<long> ValidatedPeers => _validated;
    public bool IsValidated(long peerId) => _validated.Contains(peerId);

    public void Connected(long peerId, ulong nowMs)
    {
        _validated.Remove(peerId);
        _pending[peerId] = SaturatingAdd(nowMs, _timeoutMs);
    }

    /// <summary>True only for the first Hello from a connected pending peer.</summary>
    public bool TryValidate(long peerId)
    {
        if (!_pending.Remove(peerId))
            return false;
        return _validated.Add(peerId);
    }

    public long[] Expire(ulong nowMs)
    {
        long[] expired = _pending.Where(pair => pair.Value <= nowMs).Select(pair => pair.Key).ToArray();
        foreach (long peerId in expired)
            _pending.Remove(peerId);
        return expired;
    }

    public bool Remove(long peerId)
    {
        _pending.Remove(peerId);
        return _validated.Remove(peerId);
    }

    public void Reset()
    {
        _pending.Clear();
        _validated.Clear();
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
