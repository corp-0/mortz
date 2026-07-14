using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Snapshots ride unreliable transport and arrive out of order. The buffer that
/// feeds interpolation already drops stragglers, but reconciliation ran on every
/// snapshot regardless. A late older snapshot must not rewind the local player
/// after a newer one has been accepted: the inputs it would need to replay the
/// missing interval are already pruned, so the rewind can't be undone and the
/// player mispredicts.
/// </summary>
public class StaleSnapshotTests
{
    [Fact]
    public void StaleSnapshot_ArrivingAfterANewerOne_IsIgnored()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1, server.Tick);

        // Run right for a while, reconciling each fresh (increasing-tick) snapshot.
        for (int t = 0; t < 20; t++)
        {
            predictor.LocalTick(new PlayerInput(InputButtons.Right));
            server.EnqueueInput(1, t, new PlayerInput(InputButtons.Right));
            server.Step();
            predictor.Reconcile(server.Players[1], server.Players[1].LastInputSeq, server.Tick);
        }

        Vec2 settled = predictor.State.Position;
        int ack = server.Players[1].LastInputSeq;
        int newestTick = server.Tick;

        // A straggler from five ticks ago, teleported somewhere absurd. Reconciled,
        // it would yank the player there; it must be dropped as out of order.
        PlayerState stale = server.Players[1] with { Position = new Vec2(50, 50), LastInputSeq = ack };
        Vec2 correction = predictor.Reconcile(stale, ack, newestTick - 5);

        Assert.Equal(Vec2.Zero, correction);
        Assert.Equal(settled, predictor.State.Position);
    }
}
