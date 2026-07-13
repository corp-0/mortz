namespace Mortz.Core;

/// <summary>
/// Server-side per-player input feed. Applies inputs in sequence order, one
/// per tick. Tolerates the unreliable transport: gaps (lost packets) are
/// skipped over, starvation repeats the last input, and a backlog (burst
/// after a stall) is drained at two inputs per tick so it can't add
/// permanent input latency. Draining loses the overtaken tick's movement
/// (reconciliation absorbs it) but never its button presses: they carry into
/// the next applied input, and FireSeq remembers which tick the trigger was
/// actually pulled on so shells confirm the client's predicted carve.
/// </summary>
public sealed class InputQueue
{
    /// <summary>Max pending inputs kept; older ones are skipped past.</summary>
    public const int MAX_PENDING = 4;

    private readonly SortedDictionary<int, PlayerInput> _pending = new();
    private PlayerInput _lastInput;
    /// <summary>Buttons of overtaken inputs, merged into the next applied one.</summary>
    private InputButtons _carriedButtons;
    /// <summary>Buttons of the last consumed input (applied or skipped), for
    /// press-edge detection across the whole consumed sequence.</summary>
    private InputButtons _prevConsumedButtons;

    /// <summary>Sequence of the newest input applied, acked to the client in snapshots. -1 before any.</summary>
    public int LastAppliedSeq { get; private set; } = -1;

    /// <summary>Sequence of the input that carried the newest fire press edge.
    /// Shells are stamped with this instead of LastAppliedSeq, so a press
    /// overtaken by the drain still matches the client's predicted shell.</summary>
    public int FireSeq { get; private set; } = -1;

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
            SkipNext();

        // Every input still waiting after this tick is a tick of standing
        // latency, so a backlog consumes one extra input per tick until a
        // single buffered input (jitter headroom) remains after the apply.
        if (_pending.Count > 2)
            SkipNext();
        ApplyNext();
        return _lastInput;
    }

    private void SkipNext()
    {
        int seq = FirstPendingSeq();
        Consume(seq, _pending[seq]);
        _carriedButtons |= _pending[seq].Buttons;
        _pending.Remove(seq);
    }

    private void ApplyNext()
    {
        if (_pending.Count == 0)
            return; // starvation: repeat the last input
        int seq = FirstPendingSeq();
        PlayerInput input = _pending[seq];
        _pending.Remove(seq);
        Consume(seq, input);
        _lastInput = input with { Buttons = input.Buttons | _carriedButtons };
        _carriedButtons = InputButtons.None;
        LastAppliedSeq = seq;
    }

    private void Consume(int seq, PlayerInput input)
    {
        if (input.Fire && (_prevConsumedButtons & InputButtons.Fire) == 0)
            FireSeq = seq;
        _prevConsumedButtons = input.Buttons;
    }

    private int FirstPendingSeq()
    {
        foreach (int seq in _pending.Keys)
            return seq;
        throw new InvalidOperationException("empty queue");
    }
}
