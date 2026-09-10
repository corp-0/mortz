using Mortz.Client.Replication;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Replication;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Replication;

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

        Assert.InRange(interpolator.RenderTick, 97, 98);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void IdenticalSnapshotSchedules_ProduceTheSameClockAcrossFrameRates(int fps)
    {
        Assert.Equal(RunSchedule(30).RenderTick, RunSchedule(fps).RenderTick, precision: 3);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void SnapshotOutage_HoldsAtNewestTickAndResumesWithoutRewinding(int fps)
    {
        SnapshotInterpolator interpolator = Initialized();
        float previous = interpolator.RenderTick;
        for (int frame = 0; frame < fps / 2; frame++)
        {
            interpolator.Advance(1f / fps);
            Assert.InRange(interpolator.RenderTick, previous, interpolator.NewestTick);
            previous = interpolator.RenderTick;
        }
        Assert.Equal(100, interpolator.RenderTick);

        interpolator.Add(Snapshot(110));
        interpolator.Advance(1f / fps);

        Assert.InRange(interpolator.RenderTick, 100.01f, 110);
    }

    [Fact]
    public void SplittingElapsedTimeIntoFrames_DoesNotChangeClockCorrection()
    {
        SnapshotInterpolator whole = Initialized();
        SnapshotInterpolator split = Initialized();
        whole.Add(Snapshot(102));
        split.Add(Snapshot(102));

        whole.Advance(1f / 30);
        for (int frame = 0; frame < 8; frame++)
        {
            split.Advance(1f / 240);
        }

        Assert.Equal(whole.RenderTick, split.RenderTick, precision: 4);
    }

    [Fact]
    public void InitialNegativeRenderTick_DoesNotReinitializeOnTheNextSnapshot()
    {
        SnapshotInterpolator interpolator = new();
        interpolator.Add(Snapshot(0));
        interpolator.Advance(0);
        float previous = interpolator.RenderTick;

        interpolator.Add(Snapshot(2));
        interpolator.Advance(0);

        Assert.Equal(previous, interpolator.RenderTick);
    }

    [Fact]
    public void LargeSnapshotGap_ReanchorsWithinTheNewBuffer()
    {
        SnapshotInterpolator interpolator = Initialized();
        interpolator.Add(Snapshot(300));

        interpolator.Advance(1f / 60);

        Assert.Equal(300 - NetConfig.INTERPOLATION_DELAY_TICKS, interpolator.RenderTick);
    }

    private static SnapshotInterpolator RunSchedule(int fps)
    {
        SnapshotInterpolator interpolator = Initialized();
        int framesPerSnapshot = fps / 30;
        for (int frame = 1; frame <= fps * 3; frame++)
        {
            interpolator.Advance(1f / fps);
            if (frame % framesPerSnapshot == 0)
            {
                interpolator.Add(Snapshot(100 + frame * SimConfig.TICK_RATE / fps));
            }
        }
        return interpolator;
    }

    private static SnapshotInterpolator Initialized()
    {
        SnapshotInterpolator interpolator = new();
        interpolator.Add(Snapshot(90));
        interpolator.Add(Snapshot(100));
        interpolator.Advance(0);
        return interpolator;
    }

    private static MatchSnapshot Snapshot(int tick) => new(tick, []);
}
