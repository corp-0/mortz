using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class MatchTickTests
{
    [Fact]
    public void OutputsCannotBeReadBeforeTheirProducerRuns()
    {
        MatchTick tick = NewTick();

        Assert.Throws<InvalidOperationException>(() => tick.Explosions);
        Assert.Throws<InvalidOperationException>(() => tick.Complete());
    }

    [Fact]
    public void AnOutputCanOnlyBeProducedOnce()
    {
        MatchTick tick = NewTick();
        tick.SetSimulationOutputs([], [], [], []);

        Assert.Throws<InvalidOperationException>(
            () => tick.SetSimulationOutputs([], [], [], []));
    }

    [Fact]
    public void CompletionFreezesOutputs()
    {
        MatchTick tick = NewTick();
        Explosion expected = new(10, 20, 30, 7, 4);
        Explosion[] source = [expected];
        tick.SetSimulationOutputs([], source, [], []);
        source[0] = new Explosion(1, 2, 3, 4, 5);
        tick.SetScoring([], new MatchStanding(null, 1), null);
        tick.SetParticipationChanges([]);
        tick.SetGameEvents([]);
        tick.SetEnding(null, null);
        tick.SetReturnToLobby(false);

        MatchUpdate update = tick.Complete();

        Assert.Equal(expected, Assert.Single(update.Explosions));
        Assert.Equal(new MatchStanding(null, 1), update.Standing);
        Assert.Equal(new ServerTime(100, 0.5), update.Time);
        Assert.Throws<InvalidOperationException>(() => tick.SetReturnToLobby(true));
        Assert.Throws<InvalidOperationException>(() => tick.Complete());
    }

    private static MatchTick NewTick()
    {
        TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
        SimWorld world = new(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
        MatchContext context = new(world, new Dictionary<int, Player>());
        return new MatchTick(context, new ServerTime(100, 0.5));
    }
}
