using Mortz.Client;
using Mortz.Client.Views;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Tests.Core;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>
/// Reproduces the two-shell ghost seen at real latency: the owner's predicted
/// shell lands and despawns at present time, but the authoritative replica
/// trails it by a full round trip and is still flying. Without the
/// completed-shells check, ShouldRenderAuthoritative stops hiding the replica
/// the moment the prediction leaves the seen-predicted set, so a second shell
/// pops in behind the impact and flies the tail of the same arc for about one
/// RTT. Invisible on LAN (a frame or two), obvious at 100 ms+.
/// </summary>
public class OwnerShellGhostTests
{
    private const int LOCAL_ID = 1;
    private const int ONE_WAY_TICKS = 6; // 100 ms each way at 60 tps: 200 ms RTT
    private const byte AIM_UP_LEFT = 160; // 45 degrees up-left: a long arc onto the floor

    /// <summary>
    /// After the predicted shell impacts, the late authoritative copy of the
    /// same shot must stay hidden until the server ends it.
    /// </summary>
    [Fact]
    public void OwnersAuthoritativeShell_StaysHidden_AfterThePredictionImpacts()
    {
        TerrainMask clientTerrain = TestWorlds.Flat();
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(LOCAL_ID);
        Predictor predictor = new Predictor(clientTerrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[LOCAL_ID], -1);
        MortarReplicaSet replicas = new MortarReplicaSet(clientTerrain, TestWorlds.NoSpawnProtectionConfig);

        // Wire delays, in client ticks. Client tick t and server tick t are
        // simulated in the same loop iteration; the one-way delay on each leg
        // makes the replica lag the prediction by a full RTT, like production.
        Queue<(int DeliverAt, int Seq, PlayerInput Input)> toServer = new();
        Queue<(int DeliverAt, int ServerTick, List<SimWorld.MortarEvent> Events)> toClient = new();

        int predictedImpactTick = -1;
        int firstGhostTick = -1;
        int lastGhostTick = -1;
        bool serverEnded = false;

        for (int t = 0; t < 400; t++)
        {
            // Client present time: predict input, fire once on the first tick.
            PlayerInput input = new PlayerInput(t == 0 ? InputButtons.FIRE : InputButtons.NONE, AIM_UP_LEFT);
            int seq = predictor.NextSeq;
            predictor.LocalTick(input);
            toServer.Enqueue((t + ONE_WAY_TICKS, seq, input));
            if (predictedImpactTick < 0 && predictor.DrainImpacts().Count > 0)
                predictedImpactTick = t;

            // Server, one leg behind.
            while (toServer.Count > 0 && toServer.Peek().DeliverAt <= t)
            {
                (_, int inSeq, PlayerInput inInput) = toServer.Dequeue();
                server.EnqueueInput(LOCAL_ID, inSeq, inInput);
            }
            server.Step();
            if (server.MortarEvents.Count > 0)
                toClient.Enqueue((t + ONE_WAY_TICKS, server.Tick, new List<SimWorld.MortarEvent>(server.MortarEvents)));

            // Second leg: lifecycle events reach the client's replica set.
            while (toClient.Count > 0 && toClient.Peek().DeliverAt <= t)
            {
                (_, int serverTick, List<SimWorld.MortarEvent> events) = toClient.Dequeue();
                foreach (SimWorld.MortarEvent e in events)
                {
                    switch (e.Kind)
                    {
                        case SimWorld.MortarEventKind.SPAWN:
                            replicas.Spawn(e.State, serverTick, serverTick);
                            break;
                        case SimWorld.MortarEventKind.DEFLECT:
                            replicas.Deflect(e.State, serverTick, serverTick);
                            break;
                        case SimWorld.MortarEventKind.END:
                            // Mirrors GameView.RetireEndedMortar.
                            if (replicas.TryEnd(e.State.Id, out MortarState ended) &&
                                ended.FiredBy == LOCAL_ID)
                            {
                                predictor.RetireShell(ended.SpawnSeq);
                                predictor.ForgetCompleted(ended.SpawnSeq);
                            }
                            serverEnded = true;
                            break;
                    }
                }
            }
            replicas.Tick();

            // What the view would draw this frame.
            HashSet<int> predictedSeqs = new();
            foreach ((int s, MortarState _) in predictor.Shells)
            {
                predictedSeqs.Add(s);
            }
            foreach (RenderMortar m in replicas.Render())
            {
                if (m.OwnerId != LOCAL_ID ||
                    !MortarViewManager.ShouldRenderAuthoritative(m, LOCAL_ID, predictedSeqs,
                        predictor.CompletedShells))
                    continue;
                if (firstGhostTick < 0)
                    firstGhostTick = t;
                lastGhostTick = t;
            }
        }

        // Timeline sanity: the shot flew longer than the RTT and both sides
        // finished it, otherwise the window under test never opened.
        Assert.True(predictedImpactTick > 2 * ONE_WAY_TICKS,
            $"predicted impact at tick {predictedImpactTick}; flight must outlast the RTT");
        Assert.True(serverEnded, "the server never ended the shell");

        Assert.True(firstGhostTick < 0,
            $"own authoritative shell rendered from tick {firstGhostTick} to {lastGhostTick} " +
            $"(prediction impacted at {predictedImpactTick}): the ghost second shell");
    }
}
