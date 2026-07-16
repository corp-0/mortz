using Mortz.Core.Net;
using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

/// <summary>
/// The render clock over the snapshot buffer: advances at sim speed, anchored
/// <see cref="NetConfig.INTERPOLATION_DELAY_TICKS"/> behind the newest
/// snapshot, easing drift out and snapping when hopelessly desynced
/// (join, big hitch).
/// </summary>
public sealed class SnapshotInterpolator
{
    private readonly SnapshotBuffer _snapshots = new();
    private float _renderTick = -1;

    public int NewestTick => _snapshots.NewestTick;
    public float RenderTick => _renderTick;

    public void Add(Snapshot snapshot) => _snapshots.Add(snapshot);

    /// <summary>Step the clock by one rendered frame and sample the world at it.</summary>
    public InterpolatedState? Advance(float delta)
    {
        if (_snapshots.NewestTick < 0)
            return null;
        float target = _snapshots.NewestTick - NetConfig.INTERPOLATION_DELAY_TICKS;
        if (_renderTick < 0 || MathF.Abs(target - _renderTick) > SimConfig.TICK_RATE)
            _renderTick = target;
        else
            _renderTick += delta * SimConfig.TICK_RATE + (target - _renderTick) * 0.05f;
        return _snapshots.Sample(_renderTick);
    }
}
