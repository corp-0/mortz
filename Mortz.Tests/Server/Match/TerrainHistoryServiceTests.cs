using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Match.Services;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class TerrainHistoryServiceTests
{
    [Fact]
    public void RecordsEveryExplosionFromTheMatchUpdate()
    {
        TerrainHistory history = new();
        TerrainHistoryService service = new(history);
        MatchTick tick = NewTick();
        tick.SetSimulationOutputs(
            [],
            [new Explosion(10, 20, 4, 1, 2), new Explosion(30, 40, 8, 3, 4)],
            [],
            []);
        tick.SetScoring([], new MatchStanding(null, 1), null);
        tick.SetParticipationChanges([]);
        tick.SetGameEvents([]);
        tick.SetEnding(null, null);
        tick.SetReturnToLobby(false);

        service.MatchUpdated(tick.Complete(), default);

        Assert.Equal(2, history.CarveCount);
    }

    private static MatchTick NewTick()
    {
        TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
        SimWorld world = new(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
        MatchContext context = new(world, new Dictionary<int, Player>());
        return new MatchTick(context, default);
    }
}
