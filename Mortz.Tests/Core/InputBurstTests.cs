using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Reproduction of the "[client] predicted carve seq N expired, reverting"
/// log seen in plain local play: when delivery jitter bunches input packets,
/// InputQueue's backlog drain applies two inputs in one tick and the
/// overtaken one is never simulated. A fire press on an overtaken tick is
/// either eaten outright (no server shell at all) or, if the click spans two
/// ticks, fires one seq late; both ways the client's predicted carve waits
/// for a confirmation that can never come and reverts on the ledger timeout.
/// </summary>
public class InputBurstTests
{
    private const byte AIM_UP = 192;
    private const byte AIM_DOWN = 64;

    [Fact]
    public void BurstDrain_MustNotEatAOneTickFirePress()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, new PlayerInput(InputButtons.None, AIM_UP));
        w.Step();

        // A jitter bunch: three ticks' inputs arrive at once, the click on the
        // first. The drain overtakes seq 1 with seq 2 and never simulates it.
        w.EnqueueInput(1, 1, new PlayerInput(InputButtons.Fire, AIM_UP));
        w.EnqueueInput(1, 2, new PlayerInput(InputButtons.None, AIM_UP));
        w.EnqueueInput(1, 3, new PlayerInput(InputButtons.None, AIM_UP));
        w.Step();
        w.Step();

        Assert.Single(w.Mortars); // the click must still produce the shell
    }

    [Fact]
    public void BurstDrain_MustFireAtTheSeqTheClientPredicted()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, new PlayerInput(InputButtons.None, AIM_UP));
        w.Step();

        // Same bunch, but the click spans two ticks (a normal ~30 ms press):
        // the edge survives on seq 2, so the server fires one seq late while
        // the client's predicted shell is keyed to the press tick, seq 1.
        w.EnqueueInput(1, 1, new PlayerInput(InputButtons.Fire, AIM_UP));
        w.EnqueueInput(1, 2, new PlayerInput(InputButtons.Fire, AIM_UP));
        w.EnqueueInput(1, 3, new PlayerInput(InputButtons.None, AIM_UP));
        w.Step();
        w.Step();

        MortarState shell = Assert.Single(w.Mortars);
        Assert.Equal(1, shell.SpawnSeq); // must match the client's press seq
    }

    /// <summary>
    /// The full loop, predictor included: the client predicts a point-blank
    /// shell and commits a predicted carve for its seq; the burst eats the
    /// press server-side, so no explosion with that seq ever happens and the
    /// carve ledger entry can only expire.
    /// </summary>
    [Fact]
    public void JitterBunchedDelivery_MustConfirmThePredictedCarve()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[1], -1);

        // Replays re-report impacts; the client dedups by seq (PredictCarve).
        HashSet<int> predictedImpactSeqs = new HashSet<int>();
        List<int> serverExplosionSeqs = new List<int>();

        for (int t = 0; t < 60; t++)
        {
            PlayerInput input = new PlayerInput(
                t == 10 ? InputButtons.Fire : InputButtons.None, AIM_DOWN);
            predictor.LocalTick(input);
            foreach ((int seq, Vec2 _) in predictor.DrainImpacts())
                predictedImpactSeqs.Add(seq);

            // Ticks 10-12 sit in a delayed packet and land together with 13.
            if (t < 10 || t > 13)
            {
                server.EnqueueInput(1, t, input);
            }
            else if (t == 13)
            {
                for (int seq = 10; seq <= 13; seq++)
                    server.EnqueueInput(1, seq, new PlayerInput(
                        seq == 10 ? InputButtons.Fire : InputButtons.None, AIM_DOWN));
            }
            server.Step();
            foreach ((_, _, _, _, int spawnSeq) in server.Explosions)
                serverExplosionSeqs.Add(spawnSeq);
            predictor.Reconcile(server.Players[1], server.Players[1].LastInputSeq);
        }

        int predicted = Assert.Single(predictedImpactSeqs);
        // The authoritative twin must exist, or the predicted carve expires.
        Assert.Contains(predicted, serverExplosionSeqs);
    }
}
