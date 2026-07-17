using Mortz.Core.Input;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Input;

/// <summary>
/// The lossy backlog drain. When jitter bunches inputs, InputQueue overtakes
/// ticks and only carries their button flags: the overtaken tick's aim is
/// dropped and repeated press edges collapse. FireSeq makes the carve match by
/// seq (see InputQueueTests), but the shell must still fly the way it was aimed
/// and a second click in the same bunch must not be eaten. These cover the
/// player-visible fallout: wrong-way shells, ghost carves that heal, dropped shots.
/// </summary>
public class DrainFidelityTests
{
    private const byte AIM_UP = 192;    // straight up: AimDir.Y < 0
    private const byte AIM_RIGHT = 0;   // horizontal: AimDir.Y ~ 0, so a mis-aimed
                                        // shell clears the muzzle and stays observable
                                        // instead of face-planting into the floor

    private static PlayerInput Fire(byte aim) => new(InputButtons.FIRE, aim);
    private static PlayerInput Idle(byte aim) => new(InputButtons.NONE, aim);

    /// <summary>
    /// A fire press overtaken by the drain must fire along the aim it was
    /// pressed with, not the aim of the later input that overtook it. Today the
    /// shell inherits the applied input's aim, so a shot clicked while aiming up
    /// leaves the muzzle heading down.
    /// </summary>
    [Fact]
    public void OvertakenFirePress_KeepsTheAimItWasPressedWith()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, Idle(AIM_UP));
        w.Step();

        // Bunch: the click lands aiming up on seq 1, then the aim swings aside.
        // The drain overtakes seq 1 with seq 2 and fires with seq 2's aim.
        w.EnqueueInput(1, 1, Fire(AIM_UP));
        w.EnqueueInput(1, 2, Idle(AIM_RIGHT));
        w.EnqueueInput(1, 3, Idle(AIM_RIGHT));
        w.Step();

        MortarState shell = Assert.Single(w.Mortars);
        Assert.True(shell.Velocity.Y < 0,
            $"shell must fly up (the aim at the click), got velocity {shell.Velocity}");
    }

    [Fact]
    public void OvertakenRopePress_KeepsTheAimItWasPressedWith()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, Idle(AIM_UP));
        w.Step();

        w.EnqueueInput(1, 1, new PlayerInput(InputButtons.ROPE, AIM_UP));
        w.EnqueueInput(1, 2, Idle(AIM_RIGHT));
        w.EnqueueInput(1, 3, Idle(AIM_RIGHT));
        w.Step();

        PlayerState player = w.Players[1];
        Assert.Equal(RopeMode.FLYING, player.Rope);
        Assert.True(player.RopeVelocity.Y < 0,
            $"rope must fly up (the aim at the press), got velocity {player.RopeVelocity}");
        Assert.Equal(AIM_RIGHT, player.Aim); // raw applied aim remains authoritative
    }

    [Fact]
    public void ReleaseAndSecondJumpInsideBunch_PreservesTheSecondPressEdge()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, Idle(AIM_UP));
        w.Step();

        // The first jump is overtaken by its release; the second remains pending.
        w.EnqueueInput(1, 1, new PlayerInput(InputButtons.JUMP, AIM_UP));
        w.EnqueueInput(1, 2, Idle(AIM_UP));
        w.EnqueueInput(1, 3, new PlayerInput(InputButtons.JUMP, AIM_UP));
        w.Step();
        Assert.Equal(InputButtons.NONE, w.Players[1].PrevButtons);

        w.Step();
        Assert.Equal(0, w.Players[1].JumpsLeft);
        Assert.Equal(InputButtons.JUMP, w.Players[1].PrevButtons);
    }

    /// <summary>
    /// Two separate trigger pulls delivered in one bunch must produce two
    /// shells. Today the carried Fire from the first press bleeds across the
    /// release, so the second press reads as held and never fires: one shell
    /// for two clicks.
    /// </summary>
    [Fact]
    public void TwoClicksInOneBunch_FireTwoShells()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.EnqueueInput(1, 0, Idle(AIM_UP));
        w.Step();

        // press, release, press, all arriving together, aim held up so both
        // shells climb into open sky and stay countable.
        w.EnqueueInput(1, 1, Fire(AIM_UP));
        w.EnqueueInput(1, 2, Idle(AIM_UP));
        w.EnqueueInput(1, 3, Fire(AIM_UP));
        w.Step();
        w.Step();

        Assert.Equal(2, w.Mortars.Count);
    }

    /// <summary>
    /// End to end: the authoritative shell fired across a jitter bunch must fly
    /// the way the client predicted it. When they disagree the client shows a
    /// carve where its shell landed and the server carves elsewhere, so the
    /// predicted hole heals two seconds later on the ledger timeout.
    /// </summary>
    [Fact]
    public void ServerShell_FliesTheWayTheClientPredicted_AcrossAJitterBunch()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[1], -1);

        const int FIRE_T = 30;
        Vec2 predictedVel = Vec2.Zero;
        Vec2 serverVel = Vec2.Zero;
        HashSet<int> seenMortars = new HashSet<int>();

        // Click aiming up on tick 30, then swing the aim aside. Ticks 30-33 sit
        // in one delayed packet and land together on tick 33.
        for (int t = 0; t < 60; t++)
        {
            byte aim = t <= FIRE_T ? AIM_UP : AIM_RIGHT;
            PlayerInput input = t == FIRE_T ? Fire(aim) : Idle(aim);
            predictor.LocalTick(input);
            foreach ((int seq, MortarState shell) in predictor.Shells)
            {
                if (seq == FIRE_T)
                    predictedVel = shell.Velocity;
            }

            if (t < 30 || t > 33)
                server.EnqueueInput(1, t, input);
            else if (t == 33)
            {
                server.EnqueueInput(1, 30, Fire(AIM_UP));
                server.EnqueueInput(1, 31, Idle(AIM_RIGHT));
                server.EnqueueInput(1, 32, Idle(AIM_RIGHT));
                server.EnqueueInput(1, 33, Idle(AIM_RIGHT));
            }
            server.Step();
            foreach (MortarState m in server.Mortars)
            {
                if (m.SpawnSeq == FIRE_T && seenMortars.Add(m.Id))
                    serverVel = m.Velocity;
            }

            predictor.Reconcile(server.Players[1], server.Players[1].LastInputSeq);
        }

        Assert.True(predictedVel.Y < 0, "sanity: the predicted shell climbs");
        Assert.True(serverVel.Y < 0,
            $"authoritative shell must climb like the prediction, got velocity {serverVel}");
    }

    /// <summary>
    /// End to end: every shell the client predicts must have an authoritative
    /// twin. A second click swallowed by the drain leaves the client with a
    /// predicted shell (and its carve) the server never spawned.
    /// </summary>
    [Fact]
    public void EveryPredictedShell_HasAnAuthoritativeTwin_AcrossAJitterBunch()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[1], -1);

        HashSet<int> predictedSeqs = new HashSet<int>();
        HashSet<int> serverFiredSeqs = new HashSet<int>();
        HashSet<int> seenMortars = new HashSet<int>();

        // press (30), release (31), press (32): two real clicks bunched into
        // one delayed packet that lands on tick 33. Aim held up throughout.
        PlayerInput At(int t) => t switch
        {
            30 or 32 => Fire(AIM_UP),
            _ => Idle(AIM_UP),
        };

        for (int t = 0; t < 60; t++)
        {
            predictor.LocalTick(At(t));
            foreach ((int seq, MortarState _) in predictor.Shells)
            {
                predictedSeqs.Add(seq);
            }
            foreach ((int seq, Vec2 _) in predictor.DrainImpacts())
            {
                predictedSeqs.Add(seq);
            }

            if (t < 30 || t > 33)
                server.EnqueueInput(1, t, At(t));
            else if (t == 33)
                for (int seq = 30; seq <= 33; seq++)
                {
                    server.EnqueueInput(1, seq, At(seq));
                }
            server.Step();
            foreach (MortarState m in server.Mortars)
            {
                if (seenMortars.Add(m.Id))
                    serverFiredSeqs.Add(m.SpawnSeq);
            }

            predictor.Reconcile(server.Players[1], server.Players[1].LastInputSeq);
        }

        Assert.Equal(predictedSeqs.OrderBy(s => s), serverFiredSeqs.OrderBy(s => s));
    }
}
