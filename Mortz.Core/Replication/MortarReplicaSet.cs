using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;

namespace Mortz.Core.Replication;

/// <summary>Client-side shell replicas. Reliable lifecycle establishes identity;
/// the same ballistic tick fills the gaps between compact 5 Hz corrections.</summary>
public sealed class MortarReplicaSet
{
    private readonly Dictionary<ushort, MortarState> _states = new();
    private readonly HashSet<ushort> _stopped = new();
    private readonly TerrainMask _terrain;
    private readonly MatchConfig _config;
    private bool _hasCorrection;
    private int _lastCorrectionTick;

    public MortarReplicaSet(TerrainMask terrain, MatchConfig config)
    {
        _terrain = terrain;
        _config = config;
    }

    public int Count => _states.Count;

    public void Spawn(MortarState state, int eventTick, int newestServerTick)
    {
        state.AgeTicks = 0;
        bool flying = FastForward(ref state,
            Math.Clamp(newestServerTick - eventTick, 0, SimConfig.TICK_RATE));
        _states[state.Id] = state;
        SetStopped(state.Id, flying);
    }

    public void Deflect(MortarState state, int eventTick, int newestServerTick)
    {
        if (_states.TryGetValue(state.Id, out MortarState old))
            state.AgeTicks = old.AgeTicks;
        bool flying = FastForward(ref state,
            Math.Clamp(newestServerTick - eventTick, 0, SimConfig.TICK_RATE));
        _states[state.Id] = state;
        SetStopped(state.Id, flying);
    }

    public bool TryEnd(ushort id, out MortarState state)
    {
        if (!_states.Remove(id, out state))
            return false;
        _stopped.Remove(id);
        return true;
    }

    public bool Correct(byte[] data, int correctionTick, int newestServerTick)
    {
        if (_hasCorrection && unchecked(correctionTick - _lastCorrectionTick) <= 0)
            return true; // valid but stale/duplicate unreliable delivery
        if (!MortarWire.TryReadCorrections(data,
            out List<(ushort Id, Vec2 Position, Vec2 Velocity)> corrections))
            return false;
        _hasCorrection = true;
        _lastCorrectionTick = correctionTick;
        foreach ((ushort id, Vec2 position, Vec2 velocity) in corrections)
        {
            if (!_states.TryGetValue(id, out MortarState state))
                continue; // reliable spawn has not arrived yet
            state.Position = position;
            state.Velocity = velocity;
            bool flying = FastForward(ref state,
                Math.Clamp(newestServerTick - correctionTick, 0, SimConfig.TICK_RATE));
            _states[id] = state;
            SetStopped(id, flying);
        }
        return true;
    }

    public void Tick()
    {
        foreach (ushort id in _states.Keys.ToArray())
        {
            MortarState state = _states[id];
            if (!_stopped.Contains(id))
            {
                MortarOutcome outcome = MortarSim.Tick(ref state, _terrain, _config, SimConfig.DT);
                // End is authoritative. If local quantization reaches terrain a
                // tick early, freeze at contact until End or the next correction
                // instead of making the shell disappear permanently.
                if (outcome != MortarOutcome.FLYING)
                    _stopped.Add(id);
            }
            _states[id] = state;
        }
    }

    /// <summary>Render the best present-time replica. Remote players remain
    /// interpolated in the past, but shells share the local player's gameplay
    /// timeline so an authoritative hit does not appear two body-widths short.</summary>
    public IReadOnlyList<RenderMortar> Render() => _states.Values
        .Select(state => new RenderMortar(state.Id, state.OwnerId, state.Deflected, state.SpawnSeq,
            state.Position, state.Velocity))
        .ToArray();

    private bool FastForward(ref MortarState state, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            if (MortarSim.Tick(ref state, _terrain, _config, SimConfig.DT) != MortarOutcome.FLYING)
                return false;
        return true;
    }

    private void SetStopped(ushort id, bool flying)
    {
        if (flying)
            _stopped.Remove(id);
        else
            _stopped.Add(id);
    }
}
