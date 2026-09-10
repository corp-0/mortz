using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Respawning;
using Mortz.Core.Match.Teams;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Protocol.Replication;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Sim;

public class SimWorldTests
{
    [Fact]
    public void BlockedRespawnStaysDeadUntilItsStrategyReleasesIt()
    {
        ControlledRespawns respawns = new();
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, respawns: respawns);
        world.AddPlayer(1);
        world.QueueDamage(1, byte.MaxValue);
        world.Step();
        PlayerState corpse = world.Players[1];
        Assert.Equal(0, corpse.RespawnTicks);
        Assert.False(CombatEligibility.CanTakeDamage(corpse));
        for (int i = 0; i < SimConfig.RESPAWN_DELAY_TICKS * 2; i++)
        {
            world.EnqueueInput(1, i, new PlayerInput(InputButtons.RIGHT | InputButtons.JUMP |
                InputButtons.FIRE | InputButtons.PARRY | InputButtons.ROPE));
            world.QueueDamage(1, byte.MaxValue);
            world.Step();
        }
        Assert.False(world.Players[1].IsAlive);
        Assert.Equal(corpse.Position, world.Players[1].Position);
        Assert.Empty(world.Mortars);
        Assert.Single(respawns.Deaths);
        Assert.Empty(respawns.Respawned);

        respawns.Resources = 1;
        respawns.ReturnAt = world.Tick + 2;
        world.Step();
        Assert.Equal(1, world.Players[1].RespawnTicks);
        Assert.False(world.Players[1].IsAlive);
        world.Step();
        Assert.True(world.Players[1].IsAlive);
        Assert.Equal(1, Assert.Single(respawns.Respawned));
    }

    [Fact]
    public void RespawnsConsumeSharedResourcesInPeerOrderAndRemovalEndsParticipation()
    {
        ControlledRespawns respawns = new() { Resources = 1 };
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, respawns: respawns);
        world.AddPlayer(20);
        world.AddPlayer(10);
        Assert.Equal(1, respawns.Resources);
        world.QueueDamage(20, byte.MaxValue);
        world.QueueDamage(10, byte.MaxValue);
        world.Step();
        world.Step();
        Assert.True(world.Players[10].IsAlive);
        Assert.False(world.Players[20].IsAlive);
        Assert.Equal(0, world.Players[20].RespawnTicks);
        Assert.Equal(10, Assert.Single(respawns.Respawned));

        world.QueueDamage(20, byte.MaxValue);
        world.RemovePlayer(20);
        world.RemovePlayer(20);
        Assert.Equal(20, Assert.Single(respawns.Removals));
        world.AddPlayer(20);
        world.Step();
        Assert.True(world.Players[20].IsAlive);
        Assert.Equal(3, respawns.InitialSpawns);
        Assert.Single(respawns.Respawned);
    }

    [Fact]
    public void FailedInitialSpawnAndRespawnDoNotCommitSuccess()
    {
        ControlledRespawns respawns = new() { Resources = 1 };
        FailingSpawnWorld world = new(respawns) { FailSpawn = true };
        Assert.Throws<InvalidOperationException>(() => world.AddPlayer(1));
        Assert.Equal(0, respawns.InitialSpawns);
        world.FailSpawn = false;
        world.Step();
        Assert.Equal(1, respawns.InitialSpawns);
        Assert.Equal(1, respawns.Resources);

        world.QueueDamage(1, byte.MaxValue);
        world.Step();
        world.FailSpawn = true;
        Assert.Throws<InvalidOperationException>(() => world.Step());
        Assert.False(world.Players[1].IsAlive);
        Assert.Equal(1, respawns.Resources);
        Assert.Empty(respawns.Respawned);
        world.FailSpawn = false;
        world.Step();
        Assert.True(world.Players[1].IsAlive);
        Assert.Equal(0, respawns.Resources);
        Assert.Equal(1, Assert.Single(respawns.Respawned));
    }

    [Theory]
    [InlineData("damage")]
    [InlineData("explosion")]
    [InlineData("fall")]
    public void EveryDeathPathNotifiesTheStrategyOnce(string cause)
    {
        ControlledRespawns respawns = new();
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, respawns: respawns);
        world.AddPlayer(1);
        if (cause == "fall")
        {
            world.Teleport(1, new Vec2(200, world.Terrain.Height + SimConfig.DEATH_PIT_DEPTH + 100));
        }
        if (cause == "explosion")
        {
            world.EnqueueInput(1, 0, new PlayerInput(InputButtons.FIRE, 64));
        }
        else
        {
            world.QueueDamage(1, byte.MaxValue);
            world.QueueDamage(1, byte.MaxValue);
        }
        world.Step();
        Assert.Equal(Assert.Single(world.Deaths), Assert.Single(respawns.Deaths));
        world.QueueDamage(1, byte.MaxValue);
        world.Step();
        Assert.Empty(world.Deaths);
        Assert.Single(respawns.Deaths);
    }

    public class ControlledRespawns : RespawnStrategy
    {
        public int Resources;
        public int? ReturnAt;
        public int InitialSpawns;
        public List<Death> Deaths { get; } = [];
        public List<int> Respawned { get; } = [];
        public List<int> Removals { get; } = [];
        public override void Died(Death death, int tick) => Deaths.Add(death);
        public override int? ReturnTick(int peerId, int tick) => Resources > 0 ? ReturnAt ?? tick : null;
        public override void Spawned(int peerId, bool initial)
        {
            if (initial)
            {
                InitialSpawns++;
            }
            else
            {
                Resources--;
                Respawned.Add(peerId);
            }
        }
        public override void Removed(int peerId) => Removals.Add(peerId);
    }

    public class FailingSpawnWorld(RespawnStrategy respawns)
        : SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, respawns: respawns)
    {
        public bool FailSpawn;
        protected override PlayerState FreshState(int peerId, Team? team, int lastInputSeq) =>
            FailSpawn ? throw new InvalidOperationException("Spawn failed.") : base.FreshState(peerId, team, lastInputSeq);
    }

    [Fact]
    public void SnapshotProjectionCarriesTheRequestedSkin()
    {
        MatchSnapshot snapshot = new(1,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1 }, default, Skin: 24)]);
        MatchSnapshot restored = MatchSnapshot.Deserialize(snapshot.Serialize());
        Assert.Equal(24, Assert.Single(restored.Players).Skin);
    }

    [Fact]
    public void ATeamWithTheTeamsRuleOff_IsUnconstructible()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.ProductionConfig);

        Assert.Throws<ArgumentException>(() => w.AddPlayer(1, Team.BLUE));
    }

    [Fact]
    public void TeamPlayersUseOnlyTheirOwnedSpawnPool()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules { Teams = true, SpawnImmunity = 0 },
        };
        SpawnPoint[] spawns =
        [
            new(new Vec2(100, 100), Team.BLUE),
            new(new Vec2(200, 100), Team.RED),
            new(new Vec2(300, 100), Team.BLUE),
            new(new Vec2(400, 100), Team.RED),
        ];
        SimWorld world = new(TestWorlds.Flat(), config, spawns);

        world.AddPlayer(1, Team.BLUE);
        world.AddPlayer(2, Team.RED);
        world.AddPlayer(3, Team.BLUE);
        world.AddPlayer(4, Team.RED);

        Assert.Equal(new Vec2(100, 100), world.Players[1].Position);
        Assert.Equal(new Vec2(200, 100), world.Players[2].Position);
        Assert.Equal(new Vec2(300, 100), world.Players[3].Position);
        Assert.Equal(new Vec2(400, 100), world.Players[4].Position);
    }

    [Fact]
    public void TeamWithoutOwnedSpawnsUsesNeutralPool()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules { Teams = true, SpawnImmunity = 0 },
        };
        SpawnPoint neutral = new(new Vec2(100, 100));
        SimWorld world = new(TestWorlds.Flat(), config,
            [neutral, new SpawnPoint(new Vec2(200, 100), Team.BLUE)]);

        world.AddPlayer(1, Team.RED);

        Assert.Equal(neutral.Position, world.Players[1].Position);
    }

    [Fact]
    public void TeamOwnershipIsIgnoredWhenTeamsAreOff()
    {
        SpawnPoint[] spawns =
        [
            new(new Vec2(100, 100), Team.BLUE),
            new(new Vec2(200, 100), Team.RED),
        ];
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig, spawns);

        world.AddPlayer(1);
        world.AddPlayer(2);

        Assert.Equal(spawns[0].Position, world.Players[1].Position);
        Assert.Equal(spawns[1].Position, world.Players[2].Position);
    }

    [Fact]
    public void SameInputs_ProduceIdenticalState_Determinism()
    {
        SimWorld a = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        SimWorld b = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        foreach (SimWorld w in new[] { a, b })
        {
            w.AddPlayer(1);
            w.AddPlayer(2);
        }

        // A pseudo-random but fixed input script.
        for (int t = 0; t < 600; t++)
        {
            PlayerInput i1 = new PlayerInput((InputButtons)((t / 7) % 128), (byte)(t * 5));
            PlayerInput i2 = new PlayerInput((InputButtons)((t / 11) % 128), (byte)(t * 13));
            foreach (SimWorld w in new[] { a, b })
            {
                w.EnqueueInput(1, t, i1);
                w.EnqueueInput(2, t, i2);
                w.Step();
            }
        }

        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.TakeSnapshot().Serialize(), b.TakeSnapshot().Serialize());
    }

    [Fact]
    public void PlayersSpawnStandingOnTerrain()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.AddPlayer(2);
        w.AddPlayer(1282034813); // real ENet peer ids are large: must not overflow off-map

        foreach (PlayerState p in w.Players.Values)
        {
            Assert.False(PlayerSim.BodyBlocked(w.Terrain, p.Position));
            Assert.True(PlayerSim.OnGround(w.Terrain, p.Position));
        }
    }

    [Fact]
    public void FallingOutTheBottom_Respawns()
    {
        // Floor only under the left part of the map: walking right ends in a pit.
        // Peer 1 spawns at x=241 (deterministic), on the floor.
        TerrainMask world = new TerrainMask(400, 300,
            solid: (x, y) => y >= 250 && x < 300,
            destructible: (_, _) => false);
        SimWorld w = new SimWorld(world, TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        Vec2 spawn = w.Players[1].Position;
        Assert.True(PlayerSim.OnGround(world, spawn));

        bool fell = false;
        for (int t = 0; t < 10 * SimConfig.TICK_RATE; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.RIGHT));
            w.Step();
            fell |= w.Players[1].Position.Y > 300; // below the map at some point
            if (fell) break;
        }
        Assert.True(fell);

        // A little more falling and the pit registers the death; the body then
        // lies dead below the map for the full delay before standing again.
        for (int i = 0; i < SimConfig.TICK_RATE && w.Players[1].RespawnTicks == 0; i++)
        {
            w.Step();
        }
        Assert.True(w.Players[1].RespawnTicks > 0);

        for (int i = 0; i < SimConfig.RESPAWN_DELAY_TICKS + SimConfig.TICK_RATE && !w.Players[1].Grounded; i++)
        {
            w.Step();
        }

        Assert.Equal(spawn, w.Players[1].Position);
        Assert.True(w.Players[1].Grounded);
    }

    [Fact]
    public void AddAndRemovePlayers_ReflectedInSnapshot()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(3);
        w.AddPlayer(5);
        w.Step();
        Assert.Equal(2, w.TakeSnapshot().Players.Length);

        w.RemovePlayer(3);
        w.Step();
        Snapshot snap = w.TakeSnapshot();
        Assert.Single(snap.Players);
        Assert.Equal(5, snap.Players[0].PeerId);
    }

    [Fact]
    public void SnapshotSerialization_RoundTrips()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        w.AddPlayer(1);
        w.AddPlayer(42);
        for (int t = 0; t < 30; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.RIGHT | InputButtons.JUMP, (byte)(t * 7)));
            w.Step();
        }

        Snapshot original = w.TakeSnapshot();
        Snapshot restored = SnapshotWire.Deserialize(original.Serialize());

        Assert.Equal(original.Tick, restored.Tick);
        Assert.Equal(original.Players.Length, restored.Players.Length);
        for (int i = 0; i < original.Players.Length; i++)
        {
            // PrevButtons and LastInputSeq are intentionally not on the wire.
            // Points and velocities are quantized to 1/4, so allow that much.
            Assert.Equal(original.Players[i].PeerId, restored.Players[i].PeerId);
            Assert.Equal(original.Players[i].Position.X, restored.Players[i].Position.X, 0.126f);
            Assert.Equal(original.Players[i].Position.Y, restored.Players[i].Position.Y, 0.126f);
            Assert.Equal(original.Players[i].Velocity.X, restored.Players[i].Velocity.X, 0.126f);
            Assert.Equal(original.Players[i].Velocity.Y, restored.Players[i].Velocity.Y, 0.126f);
            Assert.Equal(original.Players[i].Grounded, restored.Players[i].Grounded);
            Assert.Equal(original.Players[i].Aim, restored.Players[i].Aim);
        }
    }
}
