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
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
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

    /// <summary>
    /// Same property through the real wire format: quantization means the
    /// replay starts up to 1/8 px off, so corrections must stay in that
    /// ballpark instead of exploding tick over tick.
    /// </summary>
    [Fact]
    public void PredictionStaysTight_ThroughQuantizedWireFormat()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        for (int t = 0; t < 300; t++)
        {
            PlayerInput input = Script(t);
            predictor.LocalTick(input);
            server.EnqueueInput(1, t, input);
            server.Step();

            int ack = server.Players[1].LastInputSeq;
            PlayerState wireState = Snapshot.Deserialize(server.TakeSnapshot().Serialize()).Players[0];
            Vec2 correction = predictor.Reconcile(wireState, ack);

            Assert.True(correction.Length() < 1f, $"tick {t}: correction {correction.Length()} px");
        }
    }

    /// <summary>
    /// A snapshot can repeat an ack (the server starved for a tick, common
    /// loopback jitter). PrevButtons for the replay must survive that: if the
    /// acked input is no longer in history, a held fire button reads as a
    /// fresh press edge and the replay spawns a phantom shell the server
    /// never fired, whose predicted carve can only expire.
    /// </summary>
    [Fact]
    public void RepeatedAck_MustNotPhantomFire_WhileFireIsHeld()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        HashSet<int> predictedShellSeqs = new HashSet<int>();
        void Observe()
        {
            foreach ((int seq, _) in predictor.Shells)
                predictedShellSeqs.Add(seq);
            foreach ((int seq, _) in predictor.DrainImpacts())
                predictedShellSeqs.Add(seq);
        }

        PlayerInput held = new PlayerInput(InputButtons.Fire, 192); // fire held, aiming up
        for (int t = 0; t < 12; t++)
        {
            predictor.LocalTick(held);
            Observe();
            if (t != 1)
                server.EnqueueInput(1, t, held); // t=1's packet runs late...
            if (t == 2)
                server.EnqueueInput(1, 1, held); // ...arriving with t=2 (redundancy)
            server.Step();
            // Through the wire format: PrevButtons is not serialized, which is
            // exactly what makes the replay depend on the history anchor.
            PlayerState wireState = Snapshot.Deserialize(server.TakeSnapshot().Serialize()).Players[0];
            predictor.Reconcile(wireState, server.Players[1].LastInputSeq);
            Observe();
        }

        Assert.Equal([0], predictedShellSeqs.OrderBy(s => s)); // one press, one shell
    }

    [Fact]
    public void PredictionConverges_AfterPacketLoss()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
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

    // Up-left, so test shells die against the side wall instead of the shooter.
    private const byte AIM_UP_LEFT = 160;

    /// <summary>Firing must feel instant: the shell and the ammo change exist
    /// the same tick as the click, no server round trip involved.</summary>
    [Fact]
    public void LocalFire_SpawnsPredictedShellImmediately()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        predictor.LocalTick(new PlayerInput(InputButtons.Fire, AIM_UP_LEFT));

        (int spawnSeq, MortarState shell) = Assert.Single(predictor.Shells);
        Assert.Equal(0, spawnSeq);
        Assert.True(shell.Velocity.X < 0 && shell.Velocity.Y < 0, "flying up-left");
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO - 1, predictor.State.Ammo);
    }

    /// <summary>Weapon state and shells replayed in lockstep with the server
    /// must agree exactly, and a shell must keep flying (not jump or vanish)
    /// once its spawn input has been acked.</summary>
    [Fact]
    public void PredictedGunplay_MatchesServer_TickForTick()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        for (int t = 0; t < 240; t++)
        {
            InputButtons buttons = t switch
            {
                10 or 12 => InputButtons.Fire, // two quick shots
                30 => InputButtons.Reload,
                _ => InputButtons.None,
            };
            PlayerInput input = new PlayerInput(buttons, AIM_UP_LEFT);
            predictor.LocalTick(input);
            server.EnqueueInput(1, t, input);
            server.Step();

            PlayerState auth = server.Players[1];
            predictor.Reconcile(auth, auth.LastInputSeq);

            Assert.Equal(auth.Ammo, predictor.State.Ammo);
            Assert.Equal(auth.ReloadTicks, predictor.State.ReloadTicks);
            Assert.Equal(server.Mortars.Count, predictor.Shells.Count);
            for (int i = 0; i < server.Mortars.Count; i++)
            {
                Assert.Equal(server.Mortars[i].Position, predictor.Shells[i].Shell.Position);
                Assert.Equal(server.Mortars[i].Velocity, predictor.Shells[i].Shell.Velocity);
            }
        }
    }

    /// <summary>Same equality when acks lag behind (the real client's life):
    /// unacked shots keep being re-derived by the replay until acked, and the
    /// fast-forwarded shells still land exactly on the server's.</summary>
    [Fact]
    public void PredictedShells_StayExact_ThroughLaggedAcks()
    {
        const int LAG = 6;
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        Queue<(PlayerState State, int Ack)> wire = new();
        for (int t = 0; t < 240; t++)
        {
            InputButtons buttons = t is 40 or 44 or 48 ? InputButtons.Fire : InputButtons.None;
            PlayerInput input = new PlayerInput(buttons, AIM_UP_LEFT);
            predictor.LocalTick(input);
            server.EnqueueInput(1, t, input);
            server.Step();

            wire.Enqueue((server.Players[1], server.Players[1].LastInputSeq));
            if (wire.Count <= LAG)
                continue;
            (PlayerState old, int ack) = wire.Dequeue();
            predictor.Reconcile(old, ack);

            Assert.Equal(server.Mortars.Count, predictor.Shells.Count);
            for (int i = 0; i < server.Mortars.Count; i++)
                Assert.Equal(server.Mortars[i].Position, predictor.Shells[i].Shell.Position);
        }
    }

    /// <summary>The client carves off these events, so an impact must surface
    /// with the seq that fired the shell, and fizzles must stay silent.</summary>
    [Fact]
    public void PredictedImpact_IsReportedWithSpawnSeq()
    {
        // Destructible floor right under the player: point blank explodes instantly.
        TerrainMask world = new TerrainMask(TestWorlds.WIDTH, TestWorlds.HEIGHT,
            solid: (_, _) => false,
            destructible: (_, y) => y >= TestWorlds.FLOOR_Y);
        SimWorld server = new SimWorld(world, TestWorlds.Config);
        server.AddPlayer(1);
        Predictor predictor = new Predictor(world, TestWorlds.Config);
        predictor.Reconcile(server.Players[1], -1);

        predictor.LocalTick(new PlayerInput(InputButtons.None));
        predictor.LocalTick(new PlayerInput(InputButtons.Fire, 64)); // straight down

        (int spawnSeq, Vec2 pos) = Assert.Single(predictor.DrainImpacts());
        Assert.Equal(1, spawnSeq);
        Assert.True(pos.Y >= TestWorlds.FLOOR_Y, "impact in the floor");
        Assert.Empty(predictor.DrainImpacts()); // draining clears
        Assert.Empty(predictor.Shells);
    }

    [Fact]
    public void RestoreDestructible_UndoesACarvedPixel_ButNeverInventsGround()
    {
        TerrainMask mask = new TerrainMask(20, 20,
            solid: (x, _) => x < 5,
            destructible: (x, _) => x >= 10);

        mask.CarveCircle(12, 10, 2);
        Assert.Equal(TerrainMaterial.Empty, mask.Get(12, 10));

        mask.RestoreDestructible(12, 10);
        Assert.Equal(TerrainMaterial.Destructible, mask.Get(12, 10));

        mask.RestoreDestructible(7, 10); // always was empty: stays empty
        Assert.Equal(TerrainMaterial.Empty, mask.Get(7, 10));
        mask.RestoreDestructible(2, 10); // solid stays solid
        Assert.Equal(TerrainMaterial.Solid, mask.Get(2, 10));
    }

    [Fact]
    public void FirstReconcile_InitializesWithoutCorrection()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(7);
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);

        // Inputs recorded before the spawn snapshot arrives must not crash or offset.
        predictor.LocalTick(new PlayerInput(InputButtons.Right));
        Vec2 correction = predictor.Reconcile(server.Players[7], -1);

        Assert.Equal(Vec2.Zero, correction);
        Assert.True(predictor.Initialized);
    }

    [Fact]
    public void Reconcile_ReportsCorrectionWhenServerDisagrees()
    {
        SimWorld server = new SimWorld(TestWorlds.Flat(), TestWorlds.Config);
        server.AddPlayer(1);
        PlayerState spawn = server.Players[1];
        Predictor predictor = new Predictor(server.Terrain, TestWorlds.Config);
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
