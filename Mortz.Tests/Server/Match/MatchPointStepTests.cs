using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class MatchPointStepTests
{
    [Fact]
    public void EmitsOnlyEntryAndLapseTransitions()
    {
        Fixture fixture = new();
        Victor.Player leader = new(1);

        MatchTick enter = fixture.Advance(new MatchStanding(leader, 1));
        MatchTick steady = fixture.Advance(new MatchStanding(leader, 1));
        MatchTick lapse = fixture.Advance(new MatchStanding(null, 2));

        Assert.Equal(new MatchPointChange(new MatchPoint(1, leader)), enter.MatchPoint);
        Assert.Null(steady.MatchPoint);
        Assert.Equal(new MatchPointChange(null), lapse.MatchPoint);
        Assert.Null(fixture.MatchPoint.Active);
    }

    [Fact]
    public void WinningTheMatchLapsesAnActiveMatchPoint()
    {
        Fixture fixture = new();
        Victor.Player winner = new(1);
        fixture.Advance(new MatchStanding(winner, 1));
        new EndingStep(10).BeginVictoryLap(fixture.Context, winner);

        MatchTick won = fixture.Advance(new MatchStanding(winner, 0));

        Assert.Equal(new MatchPointChange(null), won.MatchPoint);
        Assert.Null(fixture.MatchPoint.Active);
    }

    [Fact]
    public void RequiresScoringToRunFirst()
    {
        Fixture fixture = new();

        Assert.Throws<InvalidOperationException>(() =>
            fixture.MatchPoint.Advance(new MatchTick(fixture.Context, default)));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
            SimWorld world = new(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
            Context = new MatchContext(world, new Dictionary<int, Player>());
        }

        public MatchContext Context { get; }

        public MatchPointStep MatchPoint { get; } = new();

        public MatchTick Advance(MatchStanding standing)
        {
            MatchTick tick = new(Context, default);
            tick.SetScoring([], standing, null);
            MatchPoint.Advance(tick);
            return tick;
        }
    }
}
