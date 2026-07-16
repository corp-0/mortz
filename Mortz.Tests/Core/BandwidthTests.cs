using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core;

public class BandwidthTests
{
    [Fact]
    public void RecipientSnapshot_UsesSlotsAndCompactRemoteRecords()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        for (int peer = 1; peer <= NetConfig.MAX_PLAYERS; peer++)
            world.AddPlayer(peer);

        Snapshot snapshot = world.TakeSnapshot(includeMortars: false);
        byte[] data = snapshot.SerializeFor(localPeerId: 1);
        Dictionary<byte, int> peersBySlot = snapshot.Players.ToDictionary(p => p.NetSlot, p => p.PeerId);
        Snapshot restored = Snapshot.Deserialize(data, peersBySlot);

        // 4 tick + 1 count/format + 29 local + 7*14 remote + 2 mortar count.
        Assert.Equal(134, data.Length);
        Assert.Equal(8, restored.Players.Length);
        Assert.Equal(0, restored.Players[0].Skin); // static value comes from RosterMsg
        Assert.Equal(snapshot.Players[0].PrevButtons, restored.Players[0].PrevButtons);
        Assert.Equal(snapshot.Players[0].SpawnImmunityFireThroughSeq,
            restored.Players[0].SpawnImmunityFireThroughSeq);
        Assert.Equal(snapshot.Players[1].Position, restored.Players[1].Position);
        Assert.Equal(snapshot.Players[1].SpawnImmunityTicks,
            restored.Players[1].SpawnImmunityTicks);
        Assert.Equal(0, restored.Players[1].Skin); // static value comes from RosterMsg
        Assert.Equal(Vec2.Zero, restored.Players[1].Velocity); // render-only remote record
    }

    [Fact]
    public void SnapshotMortarCount_DoesNotWrapAtByteBoundary()
    {
        MortarState[] mortars = Enumerable.Range(0, 256)
            .Select(i => new MortarState { Id = (ushort)i, Position = new Vec2(i, i) })
            .ToArray();

        Snapshot restored = Snapshot.Deserialize(new Snapshot(9, [], mortars).Serialize());

        Assert.Equal(256, restored.Mortars.Length);
        Assert.Equal((ushort)255, restored.Mortars[^1].Id);
    }

    [Fact]
    public void MortarCorrections_AreTenBytesPerShellAndRoundTrip()
    {
        MortarState[] mortars =
        [
            new() { Id = 7, Position = new Vec2(10.13f, -2.12f), Velocity = new Vec2(900, 12) },
            new() { Id = 9, Position = new Vec2(30, 40), Velocity = new Vec2(-20, 80) },
        ];

        byte[] data = MortarWire.SerializeCorrections(mortars);
        Assert.Equal(2 + mortars.Length * MortarWire.CORRECTION_BYTES_PER_SHELL, data.Length);
        Assert.True(MortarWire.TryReadCorrections(data,
            out List<(ushort Id, Vec2 Position, Vec2 Velocity)> restored));
        Assert.Equal((ushort)7, restored[0].Id);
        Assert.Equal(10.25f, restored[0].Position.X);
        Assert.False(MortarWire.TryReadCorrections(data[..^1], out _));
        Assert.False(MortarWire.TryReadCorrections([.. data, 0], out _));
    }

    [Fact]
    public void MortarLifecycle_BatchesOrderedEventsAndRejectsBadPayloads()
    {
        MortarState shell = new()
        {
            Id = 12,
            OwnerId = 7,
            FiredBy = 7,
            SpawnSeq = 99,
            Position = new Vec2(10, 20),
            Velocity = new Vec2(30, -40),
        };
        SimWorld.MortarEvent[] original =
        [
            new(SimWorld.MortarEventKind.Spawn, shell),
            new(SimWorld.MortarEventKind.Deflect, shell with { OwnerId = 8, Deflected = true }),
            new(SimWorld.MortarEventKind.End, shell),
        ];

        byte[] data = MortarWire.SerializeLifecycle(123, original);

        Assert.True(MortarWire.TryReadLifecycle(data, out int tick,
            out List<SimWorld.MortarEvent> restored));
        Assert.Equal(123, tick);
        Assert.Equal(original.Select(e => e.Kind), restored.Select(e => e.Kind));
        Assert.Equal(8, restored[1].State.OwnerId);
        Assert.Equal((ushort)12, restored[2].State.Id);
        Assert.False(MortarWire.TryReadLifecycle(data[..^1], out _, out _));
        Assert.False(MortarWire.TryReadLifecycle([.. data, 0], out _, out _));
    }

    [Fact]
    public void MortarLifecycle_LargeBurstSplitsIntoBoundedOrderedBatches()
    {
        SimWorld.MortarEvent[] original = Enumerable.Range(0, 600)
            .Select(i => new SimWorld.MortarEvent(SimWorld.MortarEventKind.Spawn,
                new MortarState { Id = (ushort)i, OwnerId = i, Position = new Vec2(i, 10) }))
            .ToArray();

        IReadOnlyList<byte[]> batches = MortarWire.SerializeLifecycleBatches(77, original);
        List<SimWorld.MortarEvent> restored = new();
        foreach (byte[] batch in batches)
        {
            Assert.True(MortarWire.TryReadLifecycle(batch, out int tick,
                out List<SimWorld.MortarEvent> events));
            Assert.Equal(77, tick);
            Assert.True(events.Count <= MortarWire.LIFECYCLE_EVENTS_PER_BATCH);
            restored.AddRange(events);
        }

        Assert.Equal(3, batches.Count);
        Assert.Equal(original.Select(e => e.State.Id), restored.Select(e => e.State.Id));
    }

    [Fact]
    public void MortarReplica_RendersOnPresentGameplayTimeline()
    {
        MatchConfig config = new() { MortarGravity = 0 };
        config.Clamp();
        MortarReplicaSet replicas = new(
            new TerrainMask(2_000, 1_000, (_, _) => false, (_, _) => false), config);
        replicas.Spawn(new MortarState
        {
            Id = 1,
            Position = new Vec2(100, 100),
            Velocity = new Vec2(60, 0), // exactly one pixel per tick
        }, eventTick: 10, newestServerTick: 10);

        Assert.Equal(100, Assert.Single(replicas.Render()).Position.X);
        replicas.Tick();
        Assert.Equal(101, Assert.Single(replicas.Render()).Position.X);
    }

    [Fact]
    public void MortarReplica_IgnoresStaleCorrectionsAndAppliesFreshImmediately()
    {
        MatchConfig config = new() { MortarGravity = 0 };
        config.Clamp();
        MortarReplicaSet replicas = new(
            new TerrainMask(2_000, 1_000, (_, _) => false, (_, _) => false), config);
        replicas.Spawn(new MortarState { Id = 1, Position = new Vec2(50, 100) }, 10, 10);

        byte[] fresh = MortarWire.SerializeCorrections(
            [new MortarState { Id = 1, Position = new Vec2(100, 100) }]);
        byte[] stale = MortarWire.SerializeCorrections(
            [new MortarState { Id = 1, Position = new Vec2(5, 100) }]);
        Assert.True(replicas.Correct(fresh, correctionTick: 12, newestServerTick: 12));
        Assert.True(replicas.Correct(stale, correctionTick: 11, newestServerTick: 12));

        Assert.Equal(100, Assert.Single(replicas.Render()).Position.X);
    }

    [Fact]
    public void MortarReplica_CorrectionOrderingSurvivesTickWrap()
    {
        MatchConfig config = new() { MortarGravity = 0 };
        config.Clamp();
        MortarReplicaSet replicas = new(
            new TerrainMask(2_000, 1_000, (_, _) => false, (_, _) => false), config);
        replicas.Spawn(new MortarState { Id = 1, Position = new Vec2(50, 100) }, 10, 10);

        Assert.True(replicas.Correct(MortarWire.SerializeCorrections(
            [new MortarState { Id = 1, Position = new Vec2(100, 100) }]),
            int.MaxValue, int.MaxValue));
        Assert.True(replicas.Correct(MortarWire.SerializeCorrections(
            [new MortarState { Id = 1, Position = new Vec2(200, 100) }]),
            int.MinValue, int.MinValue));

        Assert.Equal(200, Assert.Single(replicas.Render()).Position.X);
    }

    [Fact]
    public void MortarReplica_AuthoritativeEndRemovesImmediately()
    {
        MatchConfig config = new() { MortarGravity = 0 };
        config.Clamp();
        MortarReplicaSet replicas = new(
            new TerrainMask(2_000, 1_000, (_, _) => false, (_, _) => false), config);
        replicas.Spawn(new MortarState
        {
            Id = 1,
            Position = new Vec2(100, 100),
            Velocity = new Vec2(60, 0),
        }, 10, 10);
        Assert.Single(replicas.Render());

        Assert.True(replicas.TryEnd(1, out _));
        Assert.Empty(replicas.Render());
    }

    [Fact]
    public void ZeroGravityStationaryShell_StillExpires()
    {
        TerrainMask empty = new(100, 100, (_, _) => false, (_, _) => false);
        MatchConfig config = new() { MortarGravity = 0 };
        config.Clamp();
        MortarState shell = new() { Position = new Vec2(50, -10) };

        for (int tick = 1; tick < SimConfig.MORTAR_MAX_LIFETIME_TICKS; tick++)
            Assert.Equal(MortarOutcome.Flying, MortarSim.Tick(ref shell, empty, config, SimConfig.DT));
        Assert.Equal(MortarOutcome.Exploded, MortarSim.Tick(ref shell, empty, config, SimConfig.DT));
    }

    [Fact]
    public void ExtremeOpeningVolley_IsBoundedAndRetiresOldestShells()
    {
        MatchConfig config = new()
        {
            MortarMaxAmmo = 30,
            MortarGravity = 0,
            SpawnImmunity = 0,
        };
        config.Clamp();
        SimWorld world = new(TestWorlds.Flat(), config);
        for (int peer = 1; peer <= NetConfig.MAX_PLAYERS; peer++)
            world.AddPlayer(peer);

        int seq = 0;
        int evicted = 0;
        int forcedExplosions = 0;
        for (int shot = 0; shot < 17; shot++)
        {
            foreach (int peer in world.Players.Keys)
                world.EnqueueInput(peer, seq, new PlayerInput(InputButtons.Fire, 192));
            world.Step();
            evicted += world.MortarEvents.Count(e => e.Kind == SimWorld.MortarEventKind.End);
            forcedExplosions += world.Explosions.Count;
            seq++;
            foreach (int peer in world.Players.Keys)
                world.EnqueueInput(peer, seq, new PlayerInput(InputButtons.None, 192));
            world.Step();
            seq++;
        }

        Assert.Equal(SimConfig.MAX_ACTIVE_MORTARS, world.Mortars.Count);
        Assert.Equal(NetConfig.MAX_PLAYERS, evicted);
        Assert.Equal(NetConfig.MAX_PLAYERS, forcedExplosions);
    }

    [Fact]
    public void HybridTerrainSync_ChoosesSmallCarveLogAndRebuildsExactMask()
    {
        TerrainMask authoritative = TestWorlds.Flat(
            destructible: (x, y) => x is >= 100 and < 300 && y is >= 80 and < 240);
        TerrainCarve[] carves = [new(150, 120, 25), new(240, 170, 35)];
        foreach (TerrainCarve carve in carves)
            authoritative.CarveCircle(carve.X, carve.Y, carve.Radius);

        TerrainSyncPayload payload = TerrainSync.Build(authoritative, carves);
        TerrainMask replica = TestWorlds.Flat(
            destructible: (x, y) => x is >= 100 and < 300 && y is >= 80 and < 240);
        TerrainSync.Apply(replica, payload.Encoding, payload.Data);

        Assert.Equal(TerrainSyncEncoding.CarveLog, payload.Encoding);
        Assert.Equal(authoritative.SerializeRemoved(), replica.SerializeRemoved());
    }

    [Fact]
    public void HybridTerrainSync_SwitchesToBitmapForLongScatteredHistory()
    {
        TerrainMask authoritative = TestWorlds.Flat(
            destructible: (x, y) => x is >= 50 and < 350 && y is >= 40 and < 240);
        Random random = new(4917);
        List<TerrainCarve> carves = new();
        for (int i = 0; i < 5_000; i++)
        {
            TerrainCarve carve = new(
                (short)random.Next(50, 350), (short)random.Next(40, 240), 2);
            carves.Add(carve);
            authoritative.CarveCircle(carve.X, carve.Y, carve.Radius);
        }

        TerrainSyncPayload payload = TerrainSync.Build(authoritative, carves);
        TerrainMask replica = TestWorlds.Flat(
            destructible: (x, y) => x is >= 50 and < 350 && y is >= 40 and < 240);
        TerrainSync.Apply(replica, payload.Encoding, payload.Data);

        Assert.Equal(TerrainSyncEncoding.RemovedBitmap, payload.Encoding);
        Assert.Equal(authoritative.SerializeRemoved(), replica.SerializeRemoved());
    }
}
