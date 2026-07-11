using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class SnapshotBufferTests
{
    private static Snapshot Snap(int tick, float x) =>
        new(tick, [new PlayerState { PeerId = 1, Position = new Vec2(x, 0) }]);

    [Fact]
    public void Sample_InterpolatesBetweenBracketingSnapshots()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(Snap(10, 100));
        buf.Add(Snap(12, 200));

        InterpolatedState? mid = buf.Sample(11f);
        Assert.NotNull(mid);
        Assert.Equal(150, mid!.Players[0].Position.X, 3);
    }

    [Fact]
    public void Add_ToleratesOutOfOrderAndDuplicateArrivals()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(Snap(12, 200));
        buf.Add(Snap(10, 100)); // late arrival
        buf.Add(Snap(12, 999)); // duplicate tick: ignored

        Assert.Equal(12, buf.NewestTick);
        InterpolatedState? mid = buf.Sample(11f);
        Assert.Equal(150, mid!.Players[0].Position.X, 3);
    }

    [Fact]
    public void Sample_ClampsOutsideBufferedRange()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(Snap(10, 100));
        buf.Add(Snap(12, 200));

        Assert.Equal(100, buf.Sample(5f)!.Players[0].Position.X, 3);
        Assert.Equal(200, buf.Sample(50f)!.Players[0].Position.X, 3);
    }

    [Fact]
    public void Sample_ReturnsNullUntilTwoSnapshotsExist()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        Assert.Null(buf.Sample(0f));
        buf.Add(Snap(10, 100));
        Assert.Null(buf.Sample(10f));
    }

    [Fact]
    public void PlayerPresentOnlyInNewerSnapshot_UsesNewerPosition()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(new Snapshot(10, [new PlayerState { PeerId = 1, Position = new Vec2(100, 0) }]));
        buf.Add(new Snapshot(12,
        [
            new PlayerState { PeerId = 1, Position = new Vec2(200, 0) },
            new PlayerState { PeerId = 2, Position = new Vec2(500, 0) }, // just joined
        ]));

        InterpolatedState mid = buf.Sample(11f)!;
        Assert.Equal(500, mid.Players.First(p => p.PeerId == 2).Position.X, 3);
    }
}
