using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Replication;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.Client.Replication;

/// <summary>Client-side shell replicas. Reliable lifecycle establishes identity;
/// the same ballistic tick fills the gaps between compact 5 Hz corrections.</summary>
public sealed class MortarReplicaSet(TerrainMask terrain, Combat config, MapZones? zones = null)
{
    private readonly Dictionary<ushort, (MortarState State, int Tick, bool FromCorrection)> _states = new();
    private readonly HashSet<ushort> _stopped = new();
    private readonly MapZones _zones = zones ?? MapZones.None;
    private bool _hasCorrection;
    private int _lastCorrectionTick;

    public int Count => _states.Count;

    public void Spawn(MortarState state, int eventTick, int newestServerTick)
    {
        state.AgeTicks = 0;
        bool flying = FastForward(ref state,
            Math.Clamp(newestServerTick - eventTick, 0, SimConfig.TICK_RATE));
        _states[state.Id] = (state, eventTick, false);
        SetStopped(state.Id, flying);
    }

    public void Deflect(MortarState state, int eventTick, int newestServerTick)
    {
        if (_states.TryGetValue(state.Id, out (MortarState State, int Tick, bool FromCorrection) previous))
        {
            state.AgeTicks = previous.State.AgeTicks;
            int elapsed = unchecked(eventTick - previous.Tick);
            if (elapsed < 0 || (elapsed == 0 && previous.FromCorrection))
            {
                // Reliable ownership can arrive after a newer motion correction.
                state.Position = previous.State.Position;
                state.Velocity = previous.State.Velocity;
                _states[state.Id] = (state, previous.Tick, previous.FromCorrection);
                return;
            }
        }
        bool flying = FastForward(ref state,
            Math.Clamp(newestServerTick - eventTick, 0, SimConfig.TICK_RATE));
        _states[state.Id] = (state, eventTick, false);
        SetStopped(state.Id, flying);
    }

    public bool TryEnd(ushort id, out MortarState state)
    {
        state = default;
        if (!_states.Remove(id, out (MortarState State, int Tick, bool FromCorrection) removed))
            return false;
        state = removed.State;
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
            if (!_states.TryGetValue(id, out (MortarState State, int Tick, bool FromCorrection) previous))
                continue; // reliable spawn has not arrived yet
            if (unchecked(correctionTick - previous.Tick) < 0)
                continue;
            MortarState state = previous.State;
            state.Position = position;
            state.Velocity = velocity;
            bool flying = FastForward(ref state,
                Math.Clamp(newestServerTick - correctionTick, 0, SimConfig.TICK_RATE));
            _states[id] = (state, correctionTick, true);
            SetStopped(id, flying);
        }
        return true;
    }

    public void Tick()
    {
        foreach (ushort id in _states.Keys.ToArray())
        {
            (MortarState State, int Tick, bool FromCorrection) previous = _states[id];
            MortarState state = previous.State;
            if (!_stopped.Contains(id))
            {
                MortarOutcome outcome = MortarSim.Tick(
                    ref state, terrain, config, SimConfig.DT, _zones);
                // End is authoritative. If local quantization reaches terrain a
                // tick early, freeze at contact until End or the next correction
                // instead of making the shell disappear permanently.
                if (outcome != MortarOutcome.FLYING)
                    _stopped.Add(id);
            }
            _states[id] = (state, previous.Tick, previous.FromCorrection);
        }
    }

    /// <summary>Render the best present-time replica. Remote players remain
    /// interpolated in the past, but shells share the local player's gameplay
    /// timeline so an authoritative hit does not appear two body-widths short.</summary>
    public IReadOnlyList<RenderMortar> Render() => _states.Values
        .Select(replica => new RenderMortar(replica.State.Id, replica.State.OwnerId,
            replica.State.Deflected, replica.State.SpawnSeq, replica.State.Position, replica.State.Velocity))
        .ToArray();

    private bool FastForward(ref MortarState state, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (MortarSim.Tick(ref state, terrain, config, SimConfig.DT, _zones) !=
                MortarOutcome.FLYING)
                return false;
        }
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
