using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class PredictorTests
{
    // Cycles through every button combination (incl. dash, rope, up/down) and spins the aim.
    private static PlayerInput Script(int t) => new((InputButtons)((t / 9) % 128), (byte)(t * 11));

    /// <summary>
    /// The property everything rests on: with no packet loss, replaying acked
    /// server state + unacked inputs lands exactly on the prediction, so every
    /// reconciliation is a zero-length correction.
    /// </summary>
    [Fact]
    public void PredictionMatchesServer_WhenNothingIsLost()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat());
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain);
        predictor.Reconcile(server.Players[1], -1); // spawn state

        for (int t = 0; t < 300; t++)
        {
            PlayerInput input = Script(t);
            predictor.LocalTick(input);
            server.EnqueueInput(1, t, input);
            server.Step();

            PlayerState authoritative = server.Players[1];
            Vec2 correction = predictor.Reconcile(authoritative, authoritative.LastInputSeq);

            Assert.Equal(Vec2.Zero, correction);
            Assert.Equal(authoritative.Position, predictor.State.Position);
        }
    }

    [Fact]
    public void PredictionConverges_AfterPacketLoss()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat());
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain);
        predictor.Reconcile(server.Players[1], -1);

        // Run with every 3rd input packet lost, then go idle.
        for (int t = 0; t < 120; t++)
        {
            PlayerInput input = Script(t);
            predictor.LocalTick(input);
            if (t % 3 != 2)
                server.EnqueueInput(1, t, input);
            server.Step();
        }
        for (int t = 120; t < 240; t++)
        {
            predictor.LocalTick(new PlayerInput(InputButtons.None));
            server.EnqueueInput(1, t, new PlayerInput(InputButtons.None));
            server.Step();
        }

        PlayerState authoritative = server.Players[1];
        predictor.Reconcile(authoritative, authoritative.LastInputSeq);
        Assert.Equal(authoritative.Position, predictor.State.Position);
    }

    [Fact]
    public void FirstReconcile_InitializesWithoutCorrection()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat());
        server.AddPlayer(7);
        Predictor predictor = new Predictor(server.Terrain);

        // Inputs recorded before the spawn snapshot arrives must not crash or offset.
        predictor.LocalTick(new PlayerInput(InputButtons.Right));
        Vec2 correction = predictor.Reconcile(server.Players[7], -1);

        Assert.Equal(Vec2.Zero, correction);
        Assert.True(predictor.Initialized);
    }

    [Fact]
    public void Reconcile_ReportsCorrectionWhenServerDisagrees()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat());
        server.AddPlayer(1);
        PlayerState spawn = server.Players[1];
        Predictor predictor = new Predictor(server.Terrain);
        predictor.Reconcile(spawn, -1);

        for (int t = 0; t < 30; t++)
            predictor.LocalTick(new PlayerInput(InputButtons.Right));

        // Server acked every input but ended up back at spawn (e.g. the client
        // predicted through an obstacle it didn't know about).
        PlayerState authoritative = spawn with { LastInputSeq = 29 };
        Vec2 correction = predictor.Reconcile(authoritative, authoritative.LastInputSeq);

        Assert.NotEqual(Vec2.Zero, correction);
        Assert.Equal(authoritative.Position, predictor.State.Position);
    }
}
