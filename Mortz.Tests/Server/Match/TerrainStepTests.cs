using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class TerrainStepTests
{
    [Fact]
    public void RecordsEveryExplosionFromTheSimulationOutput()
    {
        TerrainHistory history = new();
        TerrainStep terrain = new(history);
        MatchTick tick = NewTick();
        tick.SetSimulationOutputs(
            [],
            [new Explosion(10, 20, 4, 1, 2), new Explosion(30, 40, 8, 3, 4)],
            [],
            []);

        terrain.Advance(tick);

        Assert.Equal(2, history.CarveCount);
    }

    [Fact]
    public void RequiresSimulationToRunFirst()
    {
        TerrainStep terrain = new(new TerrainHistory());

        Assert.Throws<InvalidOperationException>(() => terrain.Advance(NewTick()));
    }

    private static MatchTick NewTick()
    {
        TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
        SimWorld world = new(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
        MatchContext context = new(world, new Dictionary<int, Player>());
        return new MatchTick(context, default);
    }
}
