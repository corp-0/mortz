namespace Mortz.Core;

/// <summary>
/// Client-side prediction of the local player. Local inputs apply to a
/// private copy of the player right away so movement feels instant. On every
/// snapshot we rewind to the server's state and replay the not-yet-acked
/// inputs through the same PlayerSim.Tick the server runs. When client and
/// server agree the replay lands exactly where the prediction already was;
/// when they don't, the returned correction delta lets the view ease the
/// fix-up in instead of snapping.
/// </summary>
public sealed class Predictor
{
    private readonly InputHistory _history = new();
    private readonly TerrainMask _terrain;
    private PlayerState _state;

    /// <param name="terrain">The client's mask; carve events mutate it in place.</param>
    public Predictor(TerrainMask terrain)
    {
        _terrain = terrain;
    }

    /// <summary>False until the first snapshot containing the local player arrives.</summary>
    public bool Initialized { get; private set; }

    public PlayerState State => _state;
    public int NextSeq { get; private set; }

    /// <summary>Record and immediately apply this tick's local input.</summary>
    public void LocalTick(PlayerInput input)
    {
        _history.Add(NextSeq, input);
        if (Initialized)
            _state = PlayerSim.Tick(_state, input, _terrain);
        NextSeq++;
    }

    /// <summary>Inputs to (re)send this packet.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> RecentInputs(int n) => _history.Newest(n);

    /// <summary>
    /// Rewind to the authoritative state and replay unacked inputs.
    /// Returns how far the predicted position moved (old - new); feed it to
    /// a decaying visual offset so corrections ease in over a few frames.
    /// </summary>
    public Vec2 Reconcile(PlayerState serverState, int lastAppliedSeq)
    {
        Vec2 before = _state.Position;

        // PrevButtons is not on the wire; the acked input is ours, so restore
        // it from history to keep jump edge detection correct during replay.
        if (_history.TryGet(lastAppliedSeq, out PlayerInput acked))
            serverState.PrevButtons = acked.Buttons;

        PlayerState state = serverState;
        foreach ((int _, PlayerInput input) in _history.Since(lastAppliedSeq))
            state = PlayerSim.Tick(state, input, _terrain);

        bool wasInitialized = Initialized;
        Initialized = true;
        _state = state;
        _history.DropThrough(lastAppliedSeq);

        return wasInitialized ? before - _state.Position : Vec2.Zero;
    }
}
