using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Replication;

public class SnapshotBufferTests
{
    private static MatchSnapshot Snap(int tick, float x, byte magnitude = 0) =>
        new(tick,
        [
            new ReplicatedPlayer(
                new PlayerState { PeerId = 1, Position = new Vec2(x, 0) },
                new PlayerPresentationState { KillingSpreeMagnitude = magnitude }),
        ]);

    private static MatchSnapshot Snap(
        int tick, PlayerState[] players) =>
        new(tick, [.. players.Select(player => new ReplicatedPlayer(player, default))]);

    [Fact]
    public void Sample_InterpolatesBetweenBracketingSnapshots()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(Snap(10, 100));
        buf.Add(Snap(12, 200));

        InterpolatedState? mid = buf.Sample(11f);
        Assert.NotNull(mid);
        Assert.Equal(150, mid.Players[0].Position.X, 3);
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
    public void Sample_UsesTheOnlySnapshotUntilAnotherArrives()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        Assert.Null(buf.Sample(0f));
        buf.Add(Snap(10, 100));
        Assert.Equal(100, buf.Sample(10f)!.Players[0].Position.X, 3);
    }

    [Fact]
    public void PlayerPresentOnlyInNewerSnapshot_UsesNewerPosition()
    {
        SnapshotBuffer buf = new SnapshotBuffer();
        buf.Add(Snap(10, [new PlayerState { PeerId = 1, Position = new Vec2(100, 0) }]));
        buf.Add(Snap(12,
        [
            new PlayerState { PeerId = 1, Position = new Vec2(200, 0) },
            new PlayerState { PeerId = 2, Position = new Vec2(500, 0) }, // just joined
        ]));

        InterpolatedState mid = buf.Sample(11f)!;
        Assert.Equal(500, mid.Players.First(p => p.PeerId == 2).Position.X, 3);
    }

    [Fact]
    public void Sample_TakesSpawnImmunityFromTheNewerSnapshot()
    {
        SnapshotBuffer buffer = new();
        buffer.Add(Snap(10,
            [new PlayerState { PeerId = 1, SpawnImmunityTicks = 10 }]));
        buffer.Add(Snap(12,
            [new PlayerState { PeerId = 1, SpawnImmunityTicks = 8 }]));

        InterpolatedState sample = buffer.Sample(11)!;
        Assert.Equal(8, Assert.Single(sample.Players).SpawnImmunityTicks);
    }

    [Fact]
    public void Sample_StepsPresentationFromTheNewerBracketingSnapshot()
    {
        SnapshotBuffer buffer = new();
        buffer.Add(Snap(10, 100, magnitude: 5));
        buffer.Add(Snap(12, 200, magnitude: 7));

        RenderPlayer sampled = Assert.Single(buffer.Sample(11)!.Players);

        Assert.Equal(150, sampled.Position.X, 3);
        Assert.Equal(7, sampled.Presentation.KillingSpreeMagnitude);
        Assert.Equal(5, Assert.Single(buffer.Sample(9)!.Players)
            .Presentation.KillingSpreeMagnitude);
    }
}
