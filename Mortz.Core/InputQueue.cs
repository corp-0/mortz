namespace Mortz.Core;

/// <summary>
/// Server-side per-player input feed. Applies inputs in sequence order, one
/// per tick. Tolerates the unreliable transport: gaps (lost packets) are
/// skipped over, starvation repeats the last input, and a backlog (burst
/// after a stall) is bounded so it can't add permanent input latency.
/// </summary>
public sealed class InputQueue
{
    /// <summary>Max pending inputs kept; older ones are skipped past.</summary>
    public const int MAX_PENDING = 4;

    private readonly SortedDictionary<int, PlayerInput> _pending = new();
    private PlayerInput _lastInput;

    /// <summary>Sequence of the newest input applied, acked to the client in snapshots. -1 before any.</summary>
    public int LastAppliedSeq { get; private set; } = -1;

    public void Enqueue(int seq, PlayerInput input)
    {
        if (seq > LastAppliedSeq)
            _pending[seq] = input;
    }

    /// <summary>The input to simulate this tick.</summary>
    public PlayerInput Next()
    {
        while (_pending.Count > MAX_PENDING)
            _pending.Remove(FirstPendingSeq());

        if (_pending.Count > 0)
        {
            int seq = FirstPendingSeq();
            _lastInput = _pending[seq];
            _pending.Remove(seq);
            LastAppliedSeq = seq;
        }
        return _lastInput;
    }

    private int FirstPendingSeq()
    {
        foreach (int seq in _pending.Keys)
            return seq;
        throw new InvalidOperationException("empty queue");
    }
}
