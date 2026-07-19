using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Replication;

/// <summary>
/// The owner's predicted shells must be authoritatively retired. When the server
/// ends a shell early (a direct hit on another body, or a parry that takes it
/// over) the cosmetic copy keeps flying, carves an orphan hole the server never
/// confirms, and that hole heals two seconds later on the ledger timeout.
/// Retiring one needs three things: the shot's identity must survive a
/// deflection, survive the wire so the owner can spot it, and the predictor must
/// drop it and refuse to resurrect it on replay.
/// </summary>
public class ShellRetirementTests
{
    private const byte AIM_UP = 192;         // straight up: flies into open sky, stays alive
    private const int FIRE_TICK = 5;

    /// <summary>A deflection must keep the shot's identity so the original
    /// shooter can find and retire its own predicted copy.</summary>
    [Fact]
    public void Deflection_PreservesShotIdentity_SoTheShooterCanRetireItsPrediction()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1); // parrier, spawns at x=241
        w.AddPlayer(2); // shooter, spawns at x=130, fires level into the bubble

        int firedSeq = -1;
        bool deflected = false;
        for (int t = 0; t < 120 && !deflected; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(t >= FIRE_TICK ? InputButtons.PARRY : InputButtons.NONE));
            w.EnqueueInput(2, t, new PlayerInput(t == FIRE_TICK ? InputButtons.FIRE : InputButtons.NONE));
            w.Step();
            if (w.Mortars.Count > 0 && firedSeq < 0)
                firedSeq = w.Mortars[0].SpawnSeq; // the original shot's identity
            if (w.Mortars.Count > 0 && w.Mortars[0].Deflected)
                deflected = true;
        }

        Assert.True(deflected, "the shell should have been deflected");
        Assert.Equal(FIRE_TICK, firedSeq); // sanity: the shot was seq 5
        Assert.Equal(firedSeq, w.Mortars[0].SpawnSeq); // identity must survive the parry
        Assert.Contains((2, firedSeq), w.ShellRetirements); // reliable notice targets the original shooter
    }

    /// <summary>The shot's identity has to reach the owner: a deflected shell
    /// keeps flying (no carve event yet), so the snapshot must carry it.</summary>
    [Fact]
    public void Snapshot_RoundTripsMortarSpawnSeq()
    {
        MortarState m = new() { Id = 3, OwnerId = 7, FiredBy = 9, SpawnSeq = 42 };
        Snapshot restored = Snapshot.Deserialize(new Snapshot(5, [], [m]).Serialize());

        Assert.Equal(42, restored.Mortars[0].SpawnSeq);
        Assert.Equal(9, restored.Mortars[0].FiredBy);
    }

    /// <summary>Once the server retires a shell, the predictor must drop it and
    /// keep it dropped: a reconcile that still owns the shot in its unacked
    /// replay window must not resurrect it.</summary>
    [Fact]
    public void RetiredShell_LeavesFlight_AndNeverReturnsOnReplay()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[1], -1);

        // Fire straight up so the shell stays airborne for the whole test, then
        // never ack it: seq 0 lives inside the replay window the reconcile owns.
        predictor.LocalTick(new PlayerInput(InputButtons.FIRE, AIM_UP));
        (int seq, _) = Assert.Single(predictor.Shells);

        predictor.RetireShell(seq);
        Assert.DoesNotContain(predictor.Shells, s => s.SpawnSeq == seq);

        for (int t = 0; t < 20; t++)
        {
            predictor.LocalTick(new PlayerInput(InputButtons.NONE, AIM_UP));
            predictor.Reconcile(server.Players[1], -1); // ack stays -1: seq 0 is never acked
            Assert.DoesNotContain(predictor.Shells, s => s.SpawnSeq == seq);
        }
    }

    [Fact]
    public void RetiringShell_DiscardsItsQueuedImpact()
    {
        TerrainMask terrain = new TerrainMask(TestWorlds.WIDTH, TestWorlds.HEIGHT,
            solid: (_, _) => false,
            destructible: (_, y) => y >= TestWorlds.FLOOR_Y);
        SimWorld server = new SimWorld(terrain, TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(terrain, TestWorlds.NoSpawnProtectionConfig);
        predictor.Reconcile(server.Players[1], -1);

        predictor.LocalTick(new PlayerInput(InputButtons.FIRE, 64)); // point-blank floor impact
        predictor.RetireShell(0);

        Assert.Empty(predictor.DrainImpacts());
    }

    [Fact]
    public void Reconcile_DiscardsImpactFromInvalidatedUnackedTrajectory()
    {
        TerrainMask terrain = new TerrainMask(TestWorlds.WIDTH, TestWorlds.HEIGHT,
            solid: (_, _) => false,
            destructible: (_, y) => y >= TestWorlds.FLOOR_Y);
        SimWorld server = new SimWorld(terrain, TestWorlds.NoSpawnProtectionConfig);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(terrain, TestWorlds.NoSpawnProtectionConfig);
        PlayerState spawn = server.Players[1];
        predictor.Reconcile(spawn, -1);

        predictor.LocalTick(new PlayerInput(InputButtons.FIRE, 64));
        PlayerState corrected = spawn with
        {
            Position = spawn.Position with { Y = spawn.Position.Y - 100 },
            Grounded = false,
        };
        predictor.Reconcile(corrected, -1);

        Assert.Empty(predictor.DrainImpacts());
        Assert.Single(predictor.Shells); // rebuilt trajectory is still airborne
    }
}
