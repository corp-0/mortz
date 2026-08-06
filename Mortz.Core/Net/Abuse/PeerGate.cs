namespace Mortz.Core.Net.Abuse;

/// <summary>Everything the transport knows about a peer before and after Hello.</summary>
/// <param name="rateScale">Multiplies both budgets. 1 everywhere except an E2E
/// process running at timescale, where game time runs faster than the wall
/// clock the buckets refill against.</param>
public sealed class PeerGate(
    ulong helloTimeoutMs = NetConfig.HELLO_TIMEOUT_MS,
    double rateScale = 1)
{
    // Normal traffic is ~30 input datagrams/s and only occasional messages.
    // These bursts tolerate jitter while bounding work from any one peer.
    private const double INPUT_CAPACITY = 120;
    private const double INPUT_PER_SECOND = 60;
    private const double MESSAGE_CAPACITY = 64;
    private const double MESSAGE_PER_SECOND = 32;

    private sealed class Entry(double rateScale)
    {
        public ulong HelloDeadlineMs;
        public bool Validated;
        public RateBucket Inputs =
            new(INPUT_CAPACITY * rateScale, INPUT_PER_SECOND * rateScale);
        public RateBucket Messages =
            new(MESSAGE_CAPACITY * rateScale, MESSAGE_PER_SECOND * rateScale);
    }

    private readonly Dictionary<int, Entry> _peers = new();
    private int[] _validated = [];

    /// <summary>Stable snapshot, rebuilt only on validate/remove, so broadcast iteration
    /// never allocates.</summary>
    public IReadOnlyList<int> ValidatedPeers => _validated;

    public bool IsValidated(int peerId) => _peers.TryGetValue(peerId, out Entry? entry) && entry.Validated;

    public void Connected(int peerId, ulong nowMs)
    {
        bool wasValidated = IsValidated(peerId);
        _peers[peerId] = new Entry(rateScale)
        {
            HelloDeadlineMs = SaturatingAdd(nowMs, helloTimeoutMs),
        };
        if (wasValidated)
            RebuildValidated();
    }

    /// <summary>True only for the first Hello from a connected pending peer.</summary>
    public bool TryValidate(int peerId)
    {
        if (!_peers.TryGetValue(peerId, out Entry? entry) || entry.Validated)
            return false;
        entry.Validated = true;
        RebuildValidated();
        return true;
    }

    public int[] Expire(ulong nowMs)
    {
        int[] expired = _peers
            .Where(pair => !pair.Value.Validated && pair.Value.HelloDeadlineMs <= nowMs)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (int peerId in expired)
        {
            _peers.Remove(peerId);
        }
        return expired;
    }

    /// <summary>True when the peer was validated, i.e. the game knew about it.</summary>
    public bool Remove(int peerId)
    {
        if (!_peers.Remove(peerId, out Entry? entry))
            return false;
        if (!entry.Validated)
            return false;
        RebuildValidated();
        return true;
    }

    public void Reset()
    {
        _peers.Clear();
        _validated = [];
    }

    public bool AllowInput(int peerId, ulong nowMs) =>
        _peers.TryGetValue(peerId, out Entry? entry) && entry.Inputs.Allow(nowMs);

    public bool AllowMessage(int peerId, ulong nowMs, double cost) =>
        _peers.TryGetValue(peerId, out Entry? entry) && entry.Messages.Allow(nowMs, cost);

    private void RebuildValidated() =>
        _validated = _peers.Where(pair => pair.Value.Validated).Select(pair => pair.Key).ToArray();

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
