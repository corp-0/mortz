namespace Mortz.Core;

/// <summary>
/// Client-side prediction of the local player. Local inputs apply to a
/// private copy of the player right away so movement feels instant. On every
/// snapshot we rewind to the server's state and replay the not-yet-acked
/// inputs through the same PlayerSim.Tick the server runs. When client and
/// server agree the replay lands exactly where the prediction already was;
/// when they don't, the returned correction delta lets the view ease the
/// fix-up in instead of snapping.
///
/// Gunplay is predicted too: WeaponSim runs in the same replay (so ammo and
/// the reload bar react instantly) and every local shot spawns a cosmetic
/// shell here, keyed by the input seq that fired it. The owner only ever sees
/// these local shells; the authoritative copies in the snapshot are hidden by
/// the view. Explosions, carving and damage stay server-only: a predicted
/// shell simply vanishes on impact and the boom arrives with the carve.
/// </summary>
public sealed class Predictor
{
    private readonly InputHistory _history = new();
    private readonly TerrainMask _terrain;
    private readonly MatchConfig _cfg;
    // The local player's resolved stats; must match what the server resolved,
    // or every replay mispredicts.
    private readonly PlayerStats _stats;
    private readonly List<(int SpawnSeq, MortarState Shell)> _shells = new();
    private readonly List<(int SpawnSeq, Vec2 Position)> _impacts = new();
    private readonly HashSet<int> _retired = new();
    private int _lastReconciledTick = -1;
    private PlayerState _state;

    /// <param name="terrain">The client's mask; carve events mutate it in place.</param>
    /// <param name="config">The match config the server replicated in the Welcome.</param>
    public Predictor(TerrainMask terrain, MatchConfig config)
    {
        _terrain = terrain;
        _cfg = config;
        _stats = PlayerStats.Resolve(config);
    }

    /// <summary>False until the first snapshot containing the local player arrives.</summary>
    public bool Initialized { get; private set; }

    public PlayerState State => _state;
    public int NextSeq { get; private set; }

    /// <summary>The local player's own shells, predicted. SpawnSeq is a stable
    /// identity across reconciles (same input, same shell).</summary>
    public IReadOnlyList<(int SpawnSeq, MortarState Shell)> Shells => _shells;

    /// <summary>
    /// Predicted terrain impacts since the last drain, for predicted carving.
    /// Replays can re-report a shell's impact, so consumers must dedupe by
    /// SpawnSeq. Fizzles (out of the map) don't impact anything.
    /// </summary>
    public List<(int SpawnSeq, Vec2 Position)> DrainImpacts()
    {
        List<(int, Vec2)> drained = new(_impacts);
        _impacts.Clear();
        return drained;
    }

    /// <summary>The server ended this shell early (direct hit or parry). Drop the
    /// local copy and remember the seq so a replay can't respawn it.</summary>
    /// <returns>True if a still-flying shell was dropped.</returns>
    public bool RetireShell(int spawnSeq)
    {
        bool dropped = _shells.RemoveAll(s => s.SpawnSeq == spawnSeq) > 0;
        bool droppedImpact = _impacts.RemoveAll(i => i.SpawnSeq == spawnSeq) > 0;
        _retired.Add(spawnSeq);
        return dropped || droppedImpact;
    }

    /// <summary>True if a predicted shell for this seq is still live. The deflection
    /// retire checks this: a deflected shell is only ours if we predicted it.</summary>
    public bool HasShell(int spawnSeq)
    {
        foreach ((int seq, MortarState _) in _shells)
            if (seq == spawnSeq)
                return true;
        return false;
    }

    /// <summary>Record and immediately apply this tick's local input.</summary>
    public void LocalTick(PlayerInput input)
    {
        _history.Add(NextSeq, input);
        if (Initialized)
        {
            InputButtons prevButtons = _state.PrevButtons;
            _state = PlayerSim.Tick(_state, input, _terrain, _stats);
            if (WeaponSim.Tick(ref _state, input, prevButtons, _stats))
                _shells.Add((NextSeq, WeaponSim.NewShell((ushort)NextSeq, NextSeq, _state, input, _cfg)));
            StepShells(_shells);
        }
        NextSeq++;
    }

    /// <summary>Mirrors SimWorld's order: a shell moves on its spawn tick.
    /// Impacts are reported for predicted carving and the shell despawns; the
    /// server still owns the real consequences (authoritative carve, damage).</summary>
    private void StepShells(List<(int SpawnSeq, MortarState Shell)> shells)
    {
        for (int i = shells.Count - 1; i >= 0; i--)
        {
            (int seq, MortarState shell) = shells[i];
            MortarOutcome outcome = MortarSim.Tick(ref shell, _terrain, _cfg, SimConfig.DT);
            if (outcome == MortarOutcome.Flying)
            {
                shells[i] = (seq, shell);
                continue;
            }
            if (outcome == MortarOutcome.Exploded)
                _impacts.Add((seq, shell.Position));
            shells.RemoveAt(i);
        }
    }

    /// <summary>Inputs to (re)send this packet.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> RecentInputs(int n) => _history.Newest(n);

    /// <summary>
    /// Rewind to the authoritative state and replay unacked inputs.
    /// Returns how far the predicted position moved (old - new); feed it to
    /// a decaying visual offset so corrections ease in over a few frames.
    /// </summary>
    /// <param name="serverTick">Snapshot tick; a tick no newer than the last one
    /// reconciled is an out-of-order straggler and is dropped (replaying from
    /// already-pruned history would mispredict). -1 skips the check.</param>
    public Vec2 Reconcile(PlayerState serverState, int lastAppliedSeq, int serverTick = -1)
    {
        if (serverTick >= 0)
        {
            if (serverTick <= _lastReconciledTick)
                return Vec2.Zero;
            _lastReconciledTick = serverTick;
        }

        Vec2 before = _state.Position;

        // Shells from acked inputs stay: the server has spawned its copies,
        // but the owner keeps watching the local ones so nothing jumps back
        // in time. Shells from unacked inputs are re-derived by the replay.
        _shells.RemoveAll(s => s.SpawnSeq > lastAppliedSeq);
        // These impacts came from the unacked trajectory we are about to replace.
        // Keeping them would let the view carve a position the replay invalidated.
        _impacts.RemoveAll(i => i.SpawnSeq > lastAppliedSeq);
        List<(int SpawnSeq, MortarState Shell)> rebuilt = new();

        PlayerState state = serverState;
        foreach ((int seq, PlayerInput input) in _history.Since(lastAppliedSeq))
        {
            InputButtons prevButtons = state.PrevButtons;
            state = PlayerSim.Tick(state, input, _terrain, _stats);
            // Run the weapon even for a retired shot: its ammo and reload were
            // real, only the shell must not come back.
            if (WeaponSim.Tick(ref state, input, prevButtons, _stats) && !_retired.Contains(seq))
                rebuilt.Add((seq, WeaponSim.NewShell((ushort)seq, seq, state, input, _cfg)));
            StepShells(rebuilt); // fast-forward in lockstep with the replay
        }
        _shells.AddRange(rebuilt);

        bool wasInitialized = Initialized;
        Initialized = true;
        _state = state;
        _history.DropThrough(lastAppliedSeq);
        _retired.RemoveWhere(seq => seq <= lastAppliedSeq);

        return wasInitialized ? before - _state.Position : Vec2.Zero;
    }
}
