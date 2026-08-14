using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class SimulationStepTests
{
    [Fact]
    public void AdvancesWorldAndPublishesItsOutputs()
    {
        TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
        SimWorld world = new(terrain, new MatchConfig
        {
            Rules = new ModeRules { SpawnImmunity = 0 },
        }, Array.Empty<SpawnPoint>());
        world.AddPlayer(1);
        world.QueueDamage(1, byte.MaxValue);
        MatchContext context = new(world, new Dictionary<int, Player>());
        MatchTick tick = new(context, default);

        new SimulationStep().Advance(tick);

        Assert.Equal(1, world.Tick);
        Assert.Equal(world.MortarEvents, tick.MortarEvents);
        Assert.Equal(world.Explosions, tick.Explosions);
        Assert.Equal(world.ShellRetirements, tick.ShellRetirements);
        Assert.Equal(world.Deaths, tick.Deaths);
        Assert.Single(tick.Deaths);
    }
}
