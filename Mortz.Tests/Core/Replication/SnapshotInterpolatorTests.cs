using Mortz.Core.Net;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Replication;

public class SnapshotInterpolatorTests
{
    [Fact]
    public void RemoteRenderingStartsAtTheConfiguredDelayedTickAndAdvancesAtRenderTime()
    {
        SnapshotInterpolator interpolator = new();
        interpolator.Add(Snapshot(90));
        interpolator.Add(Snapshot(100));

        interpolator.Advance(0);
        Assert.Equal(100 - NetConfig.INTERPOLATION_DELAY_TICKS,
            interpolator.RenderTick, precision: 3);

        interpolator.Add(Snapshot(102));
        interpolator.Advance(1f / SimConfig.TICK_RATE);

        float target = 102 - NetConfig.INTERPOLATION_DELAY_TICKS;
        float expected = 100 - NetConfig.INTERPOLATION_DELAY_TICKS + 1 +
                         (target - (100 - NetConfig.INTERPOLATION_DELAY_TICKS)) * 0.05f;
        Assert.Equal(expected, interpolator.RenderTick, precision: 3);
    }

    private static MatchSnapshot Snapshot(int tick) => new(tick, []);
}
