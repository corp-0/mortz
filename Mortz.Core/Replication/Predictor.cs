using Mortz.Core.Input;
using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;

namespace Mortz.Core.Replication;

/// <summary>
/// Client-side prediction of the local player: apply inputs immediately, then
/// on every snapshot rewind to the server's state and replay unacked inputs.
/// Gunplay too: local shots spawn cosmetic shells keyed by firing input seq,
/// and the view hides the authoritative copies. Explosions, carving and
/// damage stay server-only.
/// </summary>
public sealed class Predictor
{
    private readonly InputHistory _history = new();
    private readonly TerrainMask _terrain;
    private readonly MatchConfig _cfg;
    // Stats must compose exactly like the server's or every replay
    // mispredicts. _tier1 is config + replicated persistent modifiers;
    // _effective adds the current situation on top.
    private IReadOnlyList<StatsModifier> _modifiers = [];
    private PlayerStats _tier1;
    private Situations _flags = Situations.NONE;
    private PlayerStats _effective;
    private readonly List<(int SpawnSeq, MortarState Shell)> _shells = new();
    private readonly List<(int SpawnSeq, Vec2 Position)> _impacts = new();
    private readonly HashSet<int> _retired = new();
    private readonly HashSet<int> _completed = new();
    private int _lastReconciledTick = -1;
    private PlayerState _state;

    /// <param name="terrain">The client's mask; carve events mutate it in place.</param>
    public Predictor(TerrainMask terrain, MatchConfig config)
    {
        _terrain = terrain;
        _cfg = config;
        _tier1 = PlayerStats.Resolve(config);
        _effective = _tier1;
    }

    /// <summary>Adopt the server's replicated modifier list; the next reconcile
    /// replays with it.</summary>
    public void SetModifiers(IReadOnlyList<StatsModifier> modifiers)
    {
        _modifiers = modifiers;
        _tier1 = StatsPipeline.Resolve(_cfg, modifiers);
        _effective = Compose(_flags);
    }

    /// <summary>Recomputes only when the situation flips.</summary>
    private PlayerStats Effective(in PlayerState state, in PlayerInput input)
    {
        Situations flags = SituationEffects.Detect(state, _terrain, input);
        if (flags != _flags)
        {
            _flags = flags;
            _effective = Compose(flags);
        }
        return _effective;
    }

    private PlayerStats Compose(Situations flags)
    {
        if (flags == Situations.NONE)
            return _tier1;
        List<StatsModifier> all = new(_modifiers);
        SituationEffects.AppendModifiers(flags, all);
        return StatsPipeline.Resolve(_cfg, all);
    }

    /// <summary>False until the first snapshot containing the local player arrives.</summary>
    public bool Initialized { get; private set; }

    public PlayerState State => _state;
    public int NextSeq { get; private set; }

    /// <summary>Predicted local shells. SpawnSeq is a stable identity across
    /// reconciles.</summary>
    public IReadOnlyList<(int SpawnSeq, MortarState Shell)> Shells => _shells;

    /// <summary>Shots whose ending the owner already saw. The authoritative
    /// replica trails by a round trip, so the view keeps hiding these until
    /// the server's End event clears them via ForgetCompleted.</summary>
    public IReadOnlySet<int> CompletedShells => _completed;

    /// <summary>The authoritative shell ended; its seq no longer needs hiding.</summary>
    public void ForgetCompleted(int spawnSeq) => _completed.Remove(spawnSeq);

    /// <summary>Predicted terrain impacts since the last drain. Replays can
    /// re-report an impact, so callers must dedupe by SpawnSeq.</summary>
    public List<(int SpawnSeq, Vec2 Position)> DrainImpacts()
    {
        List<(int, Vec2)> drained = new(_impacts);
        _impacts.Clear();
        return drained;
    }

    /// <summary>The server ended this shell early: drop the local copy and
    /// remember the seq so a replay can't respawn it.</summary>
    /// <returns>True if a still-flying shell was dropped.</returns>
    public bool RetireShell(int spawnSeq)
    {
        bool dropped = _shells.RemoveAll(s => s.SpawnSeq == spawnSeq) > 0;
        bool droppedImpact = _impacts.RemoveAll(i => i.SpawnSeq == spawnSeq) > 0;
        _retired.Add(spawnSeq);
        _completed.Add(spawnSeq);
        return dropped || droppedImpact;
    }

    /// <summary>True if a predicted shell for this seq is still live.</summary>
    public bool HasShell(int spawnSeq)
    {
        foreach ((int seq, MortarState _) in _shells)
        {
            if (seq == spawnSeq)
                return true;
        }
        return false;
    }

    /// <summary>Record and immediately apply this tick's local input.</summary>
    public void LocalTick(PlayerInput input)
    {
        _history.Add(NextSeq, input);
        if (Initialized)
        {
            InputButtons prevButtons = _state.PrevButtons;
            PlayerStats stats = Effective(_state, input);
            _state = PlayerSim.Tick(_state, input, _terrain, stats);
            if (WeaponSim.Tick(ref _state, input, prevButtons, stats, NextSeq))
                _shells.Add((NextSeq, WeaponSim.NewShell((ushort)NextSeq, NextSeq, _state, input, _cfg)));
            StepShells(_shells);
        }
        NextSeq++;
    }

    /// <summary>Mirrors SimWorld's order: a shell moves on its spawn tick.</summary>
    private void StepShells(List<(int SpawnSeq, MortarState Shell)> shells)
    {
        for (int i = shells.Count - 1; i >= 0; i--)
        {
            (int seq, MortarState shell) = shells[i];
            MortarOutcome outcome = MortarSim.Tick(ref shell, _terrain, _cfg, SimConfig.DT);
            if (outcome == MortarOutcome.FLYING)
            {
                shells[i] = (seq, shell);
                continue;
            }
            if (outcome == MortarOutcome.EXPLODED)
                _impacts.Add((seq, shell.Position));
            _completed.Add(seq);
            shells.RemoveAt(i);
        }
    }

    /// <summary>Inputs to (re)send this packet.</summary>
    public IReadOnlyList<(int Seq, PlayerInput Input)> RecentInputs(int n) => _history.Newest(n);

    /// <summary>
    /// Rewind to the authoritative state and replay unacked inputs. Returns
    /// how far the predicted position moved (old - new); feed it to a decaying
    /// visual offset so corrections ease in.
    /// </summary>
    /// <param name="serverTick">Out-of-order stragglers are dropped (replaying
    /// from pruned history would mispredict); -1 skips the check.</param>
    public Vec2 Reconcile(PlayerState serverState, int lastAppliedSeq, int serverTick = -1)
    {
        if (serverTick >= 0)
        {
            if (serverTick <= _lastReconciledTick)
                return Vec2.Zero;
            _lastReconciledTick = serverTick;
        }

        Vec2 before = _state.Position;

        // Acked shells stay (the owner keeps watching the local copies);
        // unacked ones are re-derived by the replay.
        _shells.RemoveAll(s => s.SpawnSeq > lastAppliedSeq);
        // Unacked impacts would carve a position the replay may invalidate.
        _impacts.RemoveAll(i => i.SpawnSeq > lastAppliedSeq);
        List<(int SpawnSeq, MortarState Shell)> rebuilt = [];

        PlayerState state = serverState;
        foreach ((int seq, PlayerInput input) in _history.Since(lastAppliedSeq))
        {
            InputButtons prevButtons = state.PrevButtons;
            PlayerStats stats = Effective(state, input);
            state = PlayerSim.Tick(state, input, _terrain, stats);
            // Run the weapon even for a retired shot: its ammo and reload were
            // real, only the shell must not come back.
            if (WeaponSim.Tick(ref state, input, prevButtons, stats, seq) && !_retired.Contains(seq))
                rebuilt.Add((seq, WeaponSim.NewShell((ushort)seq, seq, state, input, _cfg)));
            StepShells(rebuilt);
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
