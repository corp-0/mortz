using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Core.Sim;

/// <summary>The two authoritative mutations the process harness drives.</summary>
public class SimWorldMutationTests
{
    private static SimWorld World()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.NoSpawnProtectionConfig);
        world.AddPlayer(1);
        world.AddPlayer(2);
        return world;
    }

    [Fact]
    public void TeleportMovesThePlayerAndDropsTransients()
    {
        SimWorld world = World();
        world.EnqueueInput(1, 0, new PlayerInput(InputButtons.ROPE | InputButtons.DASH, 0));
        world.Step();
        Vec2 target = new(200, TestWorlds.FLOOR_Y);

        world.Teleport(1, target);

        PlayerState player = world.Players[1];
        Assert.Equal(target, player.Position);
        Assert.Equal(Vec2.Zero, player.Velocity);
        Assert.Equal(Vec2.Zero, player.RopeVelocity);
        Assert.Equal(RopeMode.NONE, player.Rope);
        Assert.Equal(0, player.RopeLength);
        Assert.Equal(0, player.DashCooldown);
        Assert.True(player.Grounded);
    }

    [Fact]
    public void TeleportIsTheSpotTheNextStepSimulatesFrom()
    {
        SimWorld world = World();
        Vec2 target = new(120, 100); // mid-air: the next tick must fall from here

        world.Teleport(1, target);
        world.Step();

        Assert.Equal(target.X, world.Players[1].Position.X);
        Assert.True(world.Players[1].Position.Y > target.Y);
    }

    [Fact]
    public void QueuedDamageLandsOnTheNextStepNotBefore()
    {
        SimWorld world = World();
        byte before = world.Players[1].Health;

        world.QueueDamage(1, 10);
        Assert.Equal(before, world.Players[1].Health);

        world.Step();
        Assert.Equal(before - 10, world.Players[1].Health);
    }

    [Fact]
    public void QueuedDamageAppliesExactlyOnce()
    {
        SimWorld world = World();
        byte before = world.Players[1].Health;

        world.QueueDamage(1, 10);
        world.Step();
        world.Step();

        Assert.Equal(before - 10, world.Players[1].Health);
    }

    [Fact]
    public void LethalQueuedDamageProducesASelfCreditedDeath()
    {
        SimWorld world = World();

        world.QueueDamage(1, 500);
        world.Step();

        Death death = Assert.Single(world.Deaths);
        Assert.Equal(1, death.PeerId);
        Assert.Equal(1, death.KillerId);
        Assert.False(death.Owned);
        Assert.Equal(-1, death.ShellId);
        Assert.Equal(0, world.Players[1].Health);
        Assert.True(world.Players[1].RespawnTicks > 0);
    }

    [Fact]
    public void QueuedDamageRespectsSpawnImmunity()
    {
        SimWorld world = new(TestWorlds.Flat(), TestWorlds.ProductionConfig);
        world.AddPlayer(1);
        byte before = world.Players[1].Health;
        Assert.True(world.Players[1].SpawnImmunityTicks > 0);

        world.QueueDamage(1, 50);
        world.Step();

        Assert.Equal(before, world.Players[1].Health);
        Assert.Empty(world.Deaths);
    }

    [Fact]
    public void QueuedDamageForAnUnknownPeerIsIgnored()
    {
        SimWorld world = World();

        world.QueueDamage(999, 50);
        world.Step();

        Assert.Empty(world.Deaths);
    }
}
