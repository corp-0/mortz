using Mortz.Core.Match;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Sim;

public class SpawnImmunityTests
{
    private const byte AIM_DOWN = 64;

    [Fact]
    public void SpawnProtection_BlocksFireForTheWholeWindow_AndComesBackOnRespawn()
    {
        MatchConfig config = new();
        SimWorld world = new(TestWorlds.Flat(), config, spawnPoints: [new Vec2(100, TestWorlds.FLOOR_Y)]);
        world.AddPlayer(1);

        Assert.Equal(SimConfig.SPAWN_IMMUNITY_TICKS, world.Players[1].SpawnImmunityTicks);

        // Clicking on the first protected tick does nothing, not even spend ammo.
        world.EnqueueInput(1, 0, new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        world.Step();
        Assert.Empty(world.Explosions);
        Assert.Empty(world.Mortars);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, world.Players[1].Ammo);

        // Release the trigger and advance to the final protected tick.
        for (int seq = 1; seq < SimConfig.SPAWN_IMMUNITY_TICKS - 1; seq++)
        {
            world.EnqueueInput(1, seq, new PlayerInput(InputButtons.NONE, AIM_DOWN));
            world.Step();
        }
        Assert.Equal(1, world.Players[1].SpawnImmunityTicks);

        // Burn the last protected tick with the trigger up, so the next seq is
        // the first one allowed to shoot.
        int finalProtectedSeq = SimConfig.SPAWN_IMMUNITY_TICKS - 1;
        world.EnqueueInput(1, finalProtectedSeq, new PlayerInput(InputButtons.NONE, AIM_DOWN));
        world.Step();
        Assert.Equal(0, world.Players[1].SpawnImmunityTicks);
        Assert.Empty(world.Explosions);

        int firstLegalSeq = SimConfig.SPAWN_IMMUNITY_TICKS;
        world.EnqueueInput(1, firstLegalSeq, new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        world.Step();
        Assert.Single(world.Explosions);
        Assert.Equal(0, world.Players[1].Health);
        Assert.True(world.Players[1].RespawnTicks > 0);

        int seqAfterDeath = firstLegalSeq + 1;
        while (world.Players[1].RespawnTicks > 0)
        {
            world.EnqueueInput(1, seqAfterDeath++, new PlayerInput(InputButtons.NONE));
            world.Step();
        }

        Assert.Equal(SimConfig.MAX_HEALTH, world.Players[1].Health);
        Assert.Equal(SimConfig.SPAWN_IMMUNITY_TICKS, world.Players[1].SpawnImmunityTicks);
        Assert.Equal(seqAfterDeath - 1 + SimConfig.SPAWN_IMMUNITY_TICKS,
            world.Players[1].SpawnImmunityFireThroughSeq);
    }

    [Fact]
    public void LateProtectedPress_MustNotFire_OnceTheTimerHasRunOut()
    {
        MatchConfig config = new() { SpawnImmunity = 2f / SimConfig.TICK_RATE };
        SimWorld world = new(TestWorlds.Flat(), config,
            spawnPoints: [new Vec2(100, TestWorlds.FLOOR_Y)]);
        world.AddPlayer(1);

        // Seq 0 goes missing for a tick, so the protected click lands right as
        // the timer hits zero.
        world.Step();
        world.EnqueueInput(1, 0, new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        world.Step();

        Assert.Equal(0, world.Players[1].SpawnImmunityTicks);
        Assert.Empty(world.Mortars);
        Assert.Empty(world.Explosions);
        Assert.Equal(SimConfig.MORTAR_MAX_AMMO, world.Players[1].Ammo);

        world.EnqueueInput(1, 1, new PlayerInput(InputButtons.NONE, AIM_DOWN));
        world.Step();
        world.EnqueueInput(1, 2, new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        world.Step();
        Assert.Single(world.Explosions);
    }

    [Fact]
    public void ProtectedLateJoiner_TakesNoBlastDamage()
    {
        Vec2[] closeSpawns =
        [
            new Vec2(100, TestWorlds.FLOOR_Y),
            new Vec2(120, TestWorlds.FLOOR_Y),
        ];
        MatchConfig protectedConfig = new();
        SimWorld protectedWorld = new(TestWorlds.Flat(), protectedConfig, spawnPoints: closeSpawns);
        protectedWorld.AddPlayer(1);

        // Let only the shooter age out of protection, then join the victim.
        for (int seq = 0; seq < SimConfig.SPAWN_IMMUNITY_TICKS; seq++)
        {
            protectedWorld.EnqueueInput(1, seq, new PlayerInput(InputButtons.NONE));
            protectedWorld.Step();
        }
        protectedWorld.AddPlayer(2);
        protectedWorld.EnqueueInput(1, SimConfig.SPAWN_IMMUNITY_TICKS,
            new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        protectedWorld.EnqueueInput(2, 0, new PlayerInput(InputButtons.NONE));
        protectedWorld.Step();

        Assert.Single(protectedWorld.Explosions);
        Assert.True(protectedWorld.Players[2].SpawnImmunityTicks > 0);
        Assert.Equal(SimConfig.MAX_HEALTH, protectedWorld.Players[2].Health);

        // Geometry guard: without protection the same blast damages the victim.
        MatchConfig vulnerableConfig = new() { SpawnImmunity = 0 };
        SimWorld vulnerableWorld = new(TestWorlds.Flat(), vulnerableConfig, spawnPoints: closeSpawns);
        vulnerableWorld.AddPlayer(1);
        vulnerableWorld.AddPlayer(2);
        vulnerableWorld.EnqueueInput(1, 0, new PlayerInput(InputButtons.FIRE, AIM_DOWN));
        vulnerableWorld.EnqueueInput(2, 0, new PlayerInput(InputButtons.NONE));
        vulnerableWorld.Step();

        Assert.True(vulnerableWorld.Players[2].Health < SimConfig.MAX_HEALTH);
    }
}
