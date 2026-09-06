using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

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
        world.Step();

        Assert.Equal(1, world.Tick);
        Assert.Single(world.Deaths);
    }
}
