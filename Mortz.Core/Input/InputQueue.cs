using Mortz.Core.Sim;

namespace Mortz.Core.Input;

/// <summary>
/// Server-side per-player input feed: applies inputs in sequence order, one
/// per tick. Gaps are skipped, starvation repeats the last input, and a
/// backlog drains at two inputs per tick so it can't add permanent latency.
/// The drain drops an overtaken tick's movement (reconciliation absorbs it)
/// but not its actions: movement buttons merge into the applied input, while
/// weapon buttons ride <see cref="Consumed"/> so the sim runs the weapon per
/// input and an overtaken fire keeps its own aim.
///
/// Admission takes one new sequence per tick, <see cref="BURST_SEQS"/> at once
/// after a stall. Sequence numbers are client-chosen, so novelty is judged
/// against what this queue actually holds, not a high-water mark the client
/// can move. A refused sequence is never queued and never acked, so the client
/// re-sends it and nothing the server acked went unsimulated.
/// </summary>
public sealed class InputQueue
{
    /// <summary>Max inputs one <see cref="Next"/> consumes: the catch-up skip
    /// plus the apply. The sim runs the weapon per consumed input, so this also
    /// caps the weapon time one tick can buy.</summary>
    public const int MAX_CONSUMED = 2;

    /// <summary>New sequences admissible at once. Refills one per tick, so a
    /// bunch covering up to this many ticks (200 ms) lands whole however late
    /// it arrives.</summary>
    public const int BURST_SEQS = 12;

    /// <summary>Overtaken buttons that merge into the applied input; weapon edges
    /// are excluded and ride the per-input <see cref="Consumed"/> list instead.</summary>
    private const InputButtons CARRIED_BUTTONS =
        InputButtons.LEFT | InputButtons.RIGHT | InputButtons.JUMP | InputButtons.DASH |
        InputButtons.ROPE | InputButtons.UP | InputButtons.DOWN | InputButtons.PARRY;

    private readonly SortedDictionary<int, PlayerInput> _pending = new();
    private int _tokens = BURST_SEQS;
    private PlayerInput _lastInput;
    private PlayerInput _rawLastInput;
    /// <summary>Movement buttons of overtaken inputs, merged into the next applied one.</summary>
    private InputButtons _carriedButtons;
    /// <summary>Buttons of the last consumed input (applied or skipped), for
    /// press-edge detection across the whole consumed sequence.</summary>
    private InputButtons _prevConsumedButtons;
    private InputButtons _pressedButtons;
    private byte? _carriedRopeAim;
    /// <summary>Inputs consumed by the most recent Next(), oldest first; raw, so
    /// each keeps its own aim.</summary>
    private readonly List<(int Seq, PlayerInput Input)> _consumed = new();

    /// <summary>Sequence of the newest input applied, acked to the client in snapshots. -1 before any.</summary>
    public int LastAppliedSeq { get; private set; } = -1;

    /// <summary>Seq of the input with the newest fire press edge; diagnostics only.</summary>
    public int FireSeq { get; private set; } = -1;

    /// <summary>Diagnostics: inputs waiting; each is a tick of added latency.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Inputs consumed by the most recent Next(), oldest first (the last
    /// is the applied one). Run the weapon over each so overtaken fires still fire.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> Consumed => _consumed;

    /// <summary>Press edges anywhere in the inputs consumed by the most recent
    /// Next(); SimWorld uses this to preserve an overtaken press even when the
    /// effective input is merged into one tick.</summary>
    public InputButtons PressedButtons => _pressedButtons;

    /// <summary>The raw input at LastAppliedSeq, before carried actions were
    /// merged. This is the authoritative edge/aim anchor for the next tick.</summary>
    public PlayerInput RawAppliedInput => _rawLastInput;

    /// <summary>Re-sends of a pending or acked sequence are free; only a new
    /// one spends a token.</summary>
    public void Enqueue(int seq, PlayerInput input)
    {
        if (seq <= LastAppliedSeq)
            return;
        if (!_pending.ContainsKey(seq))
        {
            if (_tokens == 0)
                return;
            _tokens--;
        }
        _pending[seq] = input;
    }

    /// <summary>The input to simulate this tick (movement + LastAppliedSeq).
    /// Weapon actions come from <see cref="Consumed"/>, populated alongside.</summary>
    public PlayerInput Next()
    {
        _tokens = Math.Min(BURST_SEQS, _tokens + 1);
        _consumed.Clear();
        _pressedButtons = InputButtons.NONE;
        _carriedRopeAim = null;

        // A backlog consumes one extra input per tick until a single buffered
        // input (jitter headroom) remains after the apply.
        if (_pending.Count > MAX_CONSUMED)
            SkipNext();
        ApplyNext();
        return _lastInput;
    }

    private void SkipNext()
    {
        int seq = FirstPendingSeq();
        PlayerInput input = _pending[seq];
        bool ropePressed = input.Rope && !_prevConsumedButtons.HasFlag(InputButtons.ROPE);
        Consume(seq, input);
        _carriedButtons |= input.Buttons.Only(CARRIED_BUTTONS);
        if (ropePressed && _carriedRopeAim == null)
            _carriedRopeAim = input.Aim;
        _pending.Remove(seq);
    }

    private void ApplyNext()
    {
        if (_pending.Count == 0)
        {
            // Starvation: repeat the last input for movement only. Counting it
            // as consumed would hand out free weapon time.
            _lastInput = _rawLastInput;
            return;
        }
        int seq = FirstPendingSeq();
        PlayerInput input = _pending[seq];
        _pending.Remove(seq);
        Consume(seq, input);
        _rawLastInput = input;
        _lastInput = new PlayerInput(input.Buttons | _carriedButtons, _carriedRopeAim ?? input.Aim);
        _carriedButtons = InputButtons.NONE;
        LastAppliedSeq = seq;
    }

    private void Consume(int seq, PlayerInput input)
    {
        _consumed.Add((seq, input));
        _pressedButtons |= input.Buttons.Except(_prevConsumedButtons);
        if (input.Fire && !_prevConsumedButtons.HasFlag(InputButtons.FIRE))
            FireSeq = seq;
        _prevConsumedButtons = input.Buttons;
    }

    private int FirstPendingSeq()
    {
        foreach (int seq in _pending.Keys)
        {
            return seq;
        }
        throw new InvalidOperationException("empty queue");
    }
}
