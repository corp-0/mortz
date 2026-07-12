using System.Diagnostics.CodeAnalysis;

namespace Mortz.Core;

/// <summary>
/// Bookkeeping for predicted destruction: which predicted carves still await
/// server confirmation, and which confirmed circles are recent enough to
/// guard restores. Pure logic; the view owns the actual pixels.
/// </summary>
public sealed class CarveLedger
{
    private const ulong PENDING_TIMEOUT_MS = 2000;
    /// <summary>How long confirmed circles are remembered to guard restores.</summary>
    private const ulong CONFIRM_MEMORY_MS = 3000;

    public sealed record PendingCarve(int X, int Y, int Radius, List<(int X, int Y)> Removed, ulong Expiry);

    private readonly Dictionary<int, PendingCarve> _pending = new();
    private readonly List<(int X, int Y, int Radius, ulong Expiry)> _recentConfirmed = new();

    public bool IsPending(int spawnSeq) => _pending.ContainsKey(spawnSeq);

    public void AddPending(int spawnSeq, int x, int y, int radius, List<(int X, int Y)> removed, ulong now) =>
        _pending[spawnSeq] = new PendingCarve(x, y, radius, removed, now + PENDING_TIMEOUT_MS);

    public void RecordConfirmed(int x, int y, int radius, ulong now) =>
        _recentConfirmed.Add((x, y, radius, now + CONFIRM_MEMORY_MS));

    /// <summary>The authoritative carve for a predicted shell arrived; hands back
    /// the pending entry so the view can reconcile the pixels.</summary>
    public bool TryConfirm(int spawnSeq, [NotNullWhen(true)] out PendingCarve? pending) =>
        _pending.Remove(spawnSeq, out pending);

    /// <summary>
    /// Drop confirmed circles past their memory window and collect pending
    /// carves the server never confirmed (killed before the input applied,
    /// drained input, ...); those must revert fully.
    /// </summary>
    public IReadOnlyList<(int SpawnSeq, PendingCarve Pending)> Expire(ulong now)
    {
        _recentConfirmed.RemoveAll(c => c.Expiry < now);
        List<(int SpawnSeq, PendingCarve Pending)>? expired = null;
        foreach ((int seq, PendingCarve pending) in _pending)
            if (pending.Expiry < now)
                (expired ??= new List<(int, PendingCarve)>()).Add((seq, pending));
        if (expired == null)
            return Array.Empty<(int, PendingCarve)>();
        foreach ((int seq, PendingCarve _) in expired)
            _pending.Remove(seq);
        return expired;
    }

    /// <summary>
    /// Should a pixel a mispredicted carve removed be given back? Not if the
    /// confirmed circle (or any other live carve) says it's really gone.
    /// </summary>
    public bool ShouldRestore(int px, int py, int confirmedX, int confirmedY, int confirmedRadius)
    {
        if (InsideCircle(px, py, confirmedX, confirmedY, confirmedRadius))
            return false;
        foreach ((int _, PendingCarve other) in _pending)
            if (InsideCircle(px, py, other.X, other.Y, other.Radius))
                return false;
        foreach ((int cx, int cy, int r, ulong _) in _recentConfirmed)
            if (InsideCircle(px, py, cx, cy, r))
                return false;
        return true;
    }

    private static bool InsideCircle(int px, int py, int cx, int cy, int radius)
    {
        int dx = px - cx, dy = py - cy;
        return radius >= 0 && dx * dx + dy * dy <= radius * radius;
    }
}
