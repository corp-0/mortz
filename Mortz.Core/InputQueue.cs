namespace Mortz.Core;

/// <summary>
/// Server-side per-player input feed. Applies inputs in sequence order, one
/// per tick. Tolerates the unreliable transport: gaps (lost packets) are
/// skipped over, starvation repeats the last input, and a backlog (burst
/// after a stall) is drained at two inputs per tick so it can't add
/// permanent input latency.
/// </summary>
public sealed class InputQueue
{
    /// <summary>Max pending inputs kept; older ones are skipped past.</summary>
    public const int MAX_PENDING = 4;

    private readonly SortedDictionary<int, PlayerInput> _pending = new();
    private PlayerInput _lastInput;

    /// <summary>Sequence of the newest input applied, acked to the client in snapshots. -1 before any.</summary>
    public int LastAppliedSeq { get; private set; } = -1;

    /// <summary>Diagnostics: inputs waiting to be applied. Every pending input is a tick of added latency.</summary>
    public int PendingCount => _pending.Count;

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

        // Every input still waiting after this tick is a tick of standing
        // latency, so a backlog consumes one extra input per tick until a
        // single buffered input (jitter headroom) remains. The overtaken
        // input's tick of movement is lost; reconciliation absorbs it.
        ApplyNext();
        if (_pending.Count > 1)
            ApplyNext();
        return _lastInput;
    }

    private void ApplyNext()
    {
        if (_pending.Count == 0)
            return;
        int seq = FirstPendingSeq();
        _lastInput = _pending[seq];
        _pending.Remove(seq);
        LastAppliedSeq = seq;
    }

    private int FirstPendingSeq()
    {
        foreach (int seq in _pending.Keys)
            return seq;
        throw new InvalidOperationException("empty queue");
    }
}
