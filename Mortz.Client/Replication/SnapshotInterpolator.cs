using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Replication;

namespace Mortz.Client.Replication;

/// <summary>
/// The render clock over the snapshot buffer: advances at sim speed, anchored
/// <see cref="NetConfig.INTERPOLATION_DELAY_TICKS"/> behind the newest
/// snapshot, easing drift out and snapping when hopelessly desynced
/// (join, big hitch).
/// </summary>
public sealed class SnapshotInterpolator
{
    private readonly SnapshotBuffer _snapshots = new();
    private const double DRIFT_CORRECTION_PER_SECOND = 3;
    private double _renderTick;
    private bool _initialized;

    public int NewestTick => _snapshots.NewestTick;
    public float RenderTick => _initialized ? (float)_renderTick : -1;

    public void Add(MatchSnapshot snapshot) => _snapshots.Add(snapshot);

    /// <summary>Step the clock by one rendered frame and sample the world at it.</summary>
    public InterpolatedState? Advance(float delta)
    {
        if (_snapshots.NewestTick < 0)
            return null;
        double target = _snapshots.NewestTick - NetConfig.INTERPOLATION_DELAY_TICKS;
        if (!_initialized || Math.Abs(target - _renderTick) > SimConfig.TICK_RATE)
        {
            _renderTick = target;
            _initialized = true;
        }
        else
        {
            // Integrate drift correction over elapsed time so splitting a frame doesn't change the clock.
            double equilibrium = target + SimConfig.TICK_RATE / DRIFT_CORRECTION_PER_SECOND;
            _renderTick = equilibrium + (_renderTick - equilibrium) *
                Math.Exp(-DRIFT_CORRECTION_PER_SECOND * delta);
        }
        // Exhausting the buffer holds the newest state until another snapshot arrives.
        _renderTick = Math.Min(_renderTick, _snapshots.NewestTick);
        return _snapshots.Sample((float)_renderTick);
    }
}
