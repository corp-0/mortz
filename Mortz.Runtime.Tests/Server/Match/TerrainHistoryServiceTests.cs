using Mortz.Core.Sim;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Match.Services;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class TerrainHistoryServiceTests
{
    [Fact]
    public void RecordsEveryExplosionFromTheMatchUpdate()
    {
        TerrainHistory history = new();
        TerrainHistoryService service = new(history);
        MatchUpdate update = new(1, default, [],
            [new Explosion(10, 20, 4, 1, 2), new Explosion(30, 40, 8, 3, 4)],
            [], [], [], new MatchStanding(null, 1), [], [], null, null, false);
        service.MatchUpdated(update, default);

        Assert.Equal(2, history.CarveCount);
    }

}
