using Mortz.Core.Sim;

namespace Mortz.Core.Input;

/// <summary>
/// Server-side per-player input feed. Applies inputs in sequence order, one
/// per tick. Tolerates the unreliable transport: gaps (lost packets) are
/// skipped over, starvation repeats the last input, and a backlog (burst
/// after a stall) is drained at two inputs per tick so it can't add permanent
/// input latency.
///
/// The drain drops the overtaken tick's movement (reconciliation absorbs it)
/// but not its actions. Movement buttons of overtaken inputs merge into the
/// applied one so a jump or dash still acts. Weapon buttons (Fire/Reload) don't
/// merge: every consumed input is exposed in <see cref="Consumed"/> so the sim
/// runs the weapon per input, an overtaken fire keeps its own aim, and the
/// applied input stays an honest PrevButtons anchor for the owner's replay.
/// </summary>
public sealed class InputQueue
{
    /// <summary>Max pending inputs kept; older ones are skipped past.</summary>
    public const int MAX_PENDING = 4;

    /// <summary>Overtaken buttons that merge into the applied input; weapon edges
    /// are excluded and ride the per-input <see cref="Consumed"/> list instead.</summary>
    private const InputButtons CARRIED_BUTTONS =
        InputButtons.LEFT | InputButtons.RIGHT | InputButtons.JUMP | InputButtons.DASH |
        InputButtons.ROPE | InputButtons.UP | InputButtons.DOWN | InputButtons.PARRY;

    private readonly SortedDictionary<int, PlayerInput> _pending = new();
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

    /// <summary>Sequence of the input that carried the newest fire press edge.
    /// Diagnostics only now that shells are stamped per consumed input.</summary>
    public int FireSeq { get; private set; } = -1;

    /// <summary>Diagnostics: inputs waiting to be applied. Every pending input is a tick of added latency.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Inputs consumed by the most recent Next(), oldest first (the last
    /// is the applied one). Run the weapon over each so overtaken fires still fire.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> Consumed => _consumed;

    /// <summary>Press edges present anywhere in the inputs consumed by the most
    /// recent Next(). SimWorld uses this to preserve an overtaken release/press
    /// transition even when the effective input is merged into one tick.</summary>
    public InputButtons PressedButtons => _pressedButtons;

    /// <summary>The raw input at LastAppliedSeq, before carried actions were
    /// merged. This is the authoritative edge/aim anchor for the next tick.</summary>
    public PlayerInput RawAppliedInput => _rawLastInput;

    public void Enqueue(int seq, PlayerInput input)
    {
        if (seq > LastAppliedSeq)
            _pending[seq] = input;
    }

    /// <summary>The input to simulate this tick (movement + LastAppliedSeq).
    /// Weapon actions come from <see cref="Consumed"/>, populated alongside.</summary>
    public PlayerInput Next()
    {
        _consumed.Clear();
        _pressedButtons = InputButtons.NONE;
        _carriedRopeAim = null;

        while (_pending.Count > MAX_PENDING)
        {
            SkipNext();
        }

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
        PlayerInput input = _pending[seq];
        bool ropePressed = input.Rope && (_prevConsumedButtons & InputButtons.ROPE) == 0;
        Consume(seq, input);
        _carriedButtons |= input.Buttons & CARRIED_BUTTONS;
        if (ropePressed && _carriedRopeAim == null)
            _carriedRopeAim = input.Aim;
        _pending.Remove(seq);
    }

    private void ApplyNext()
    {
        if (_pending.Count == 0)
        {
            // Starvation: repeat the last input. It still counts as consumed so
            // reload keeps ticking; held buttons produce no new edge.
            _lastInput = _rawLastInput;
            _consumed.Add((LastAppliedSeq, _lastInput));
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
        _pressedButtons |= input.Buttons & ~_prevConsumedButtons;
        if (input.Fire && (_prevConsumedButtons & InputButtons.FIRE) == 0)
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
