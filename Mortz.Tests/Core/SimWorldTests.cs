using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class SimWorldTests
{
    [Fact]
    public void SameInputs_ProduceIdenticalState_Determinism()
    {
        SimWorld a = new SimWorld(TestWorlds.Flat());
        SimWorld b = new SimWorld(TestWorlds.Flat());
        foreach (SimWorld? w in new[] { a, b })
        {
            w.AddPlayer(1);
            w.AddPlayer(2);
        }

        // A pseudo-random but fixed input script.
        for (int t = 0; t < 600; t++)
        {
            PlayerInput i1 = new PlayerInput((InputButtons)((t / 7) % 128), (byte)(t * 5));
            PlayerInput i2 = new PlayerInput((InputButtons)((t / 11) % 128), (byte)(t * 13));
            foreach (SimWorld? w in new[] { a, b })
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
        SimWorld w = new SimWorld(TestWorlds.Flat());
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
        SimWorld w = new SimWorld(world);
        w.AddPlayer(1);
        Vec2 spawn = w.Players[1].Position;
        Assert.True(PlayerSim.OnGround(world, spawn));

        bool fell = false;
        for (int t = 0; t < 10 * SimConfig.TICK_RATE; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.Right));
            w.Step();
            fell |= w.Players[1].Position.Y > 300; // below the map at some point
            if (fell) break;
        }
        Assert.True(fell);

        // A little more falling and the pit registers the death; the body then
        // lies dead below the map for the full delay before standing again.
        for (int i = 0; i < SimConfig.TICK_RATE && w.Players[1].RespawnTicks == 0; i++)
            w.Step();
        Assert.True(w.Players[1].RespawnTicks > 0);

        for (int i = 0; i < SimConfig.RESPAWN_DELAY_TICKS + SimConfig.TICK_RATE && !w.Players[1].Grounded; i++)
            w.Step();

        Assert.Equal(spawn, w.Players[1].Position);
        Assert.True(w.Players[1].Grounded);
    }

    [Fact]
    public void AddAndRemovePlayers_ReflectedInSnapshot()
    {
        SimWorld w = new SimWorld(TestWorlds.Flat());
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
        SimWorld w = new SimWorld(TestWorlds.Flat());
        w.AddPlayer(1);
        w.AddPlayer(42);
        for (int t = 0; t < 30; t++)
        {
            w.EnqueueInput(1, t, new PlayerInput(InputButtons.Right | InputButtons.Jump, (byte)(t * 7)));
            w.Step();
        }

        Snapshot original = w.TakeSnapshot();
        Snapshot restored = Snapshot.Deserialize(original.Serialize());

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
            Assert.Equal(original.Players[i].Skin, restored.Players[i].Skin);
        }
    }
}
