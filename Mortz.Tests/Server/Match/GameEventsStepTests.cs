using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Events;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class GameEventsStepTests
{
    [Fact]
    public void ProducesJudgmentsFromScoredEliminations()
    {
        Fixture fixture = new();
        MatchTick tick = fixture.NewTick();
        tick.SetScoring(
            [fixture.Kill(1, 2, firstBlood: true)],
            new MatchStanding(null, 1),
            null);

        fixture.GameEvents.Advance(tick);

        Assert.Contains(
            new Judgment(GameEventKind.FIRST_BLOOD, 1, 2, 0),
            tick.GameEvents);
        Assert.Contains(
            new Judgment(GameEventKind.REGULAR_KILL, 1, 2, 0),
            tick.GameEvents);
    }

    [Fact]
    public void ProjectsTheCurrentKillingSpreeMagnitude()
    {
        Fixture fixture = new();

        fixture.Advance([fixture.Kill(1, 2)]);

        Assert.Equal(1, fixture.GameEvents.KillingSpreeMagnitude(fixture.Player(1)));
        Assert.Equal(0, fixture.GameEvents.KillingSpreeMagnitude(fixture.Player(2)));
    }

    [Fact]
    public void RequiresScoringToRunFirst()
    {
        Fixture fixture = new();

        Assert.Throws<InvalidOperationException>(() =>
            fixture.GameEvents.Advance(fixture.NewTick()));
    }

    [Fact]
    public void RemovingAPlayerScrubsGrudges()
    {
        Fixture fixture = new();
        fixture.Advance([fixture.Kill(1, 2)]);
        Player leaver = fixture.Player(1);

        fixture.Cells.Leave(leaver);
        fixture.GameEvents.PlayerLeft(leaver);
        IReadOnlyList<Judgment> afterRejoin = fixture.Advance([fixture.Kill(2, 1)]);

        Assert.DoesNotContain(afterRejoin, judgment =>
            judgment.Kind == GameEventKind.REVENGE);
    }

    private sealed class Fixture
    {
        private readonly SimWorld _world;

        public Fixture()
        {
            TerrainMask terrain = new(32, 32, (_, _) => false, (_, _) => false);
            _world = new SimWorld(terrain, new MatchConfig(), Array.Empty<SpawnPoint>());
            GameEventJudge judge = new(Cells.Keys, Cells.Seated, _ => null);
            GameEvents = new GameEventsStep(judge);
        }

        public MatchCells Cells { get; } = new();

        public GameEventsStep GameEvents { get; }

        public Player Player(int peerId) => Cells.GetOrJoin(peerId);

        public MatchTick NewTick() =>
            new(new MatchContext(_world, Cells.Seated), default);

        public IReadOnlyList<Judgment> Advance(IReadOnlyList<ScoredKill> eliminations)
        {
            MatchTick tick = NewTick();
            tick.SetScoring(eliminations, new MatchStanding(null, 1), null);
            GameEvents.Advance(tick);
            return tick.GameEvents;
        }

        public ScoredKill Kill(int killerId, int victimId, bool firstBlood = false) =>
            new(
                Player(killerId),
                Player(victimId),
                new DeathScore(
                    0,
                    0,
                    DeathKind.KILL,
                    null,
                    default,
                    null,
                    default,
                    null),
                Owned: false,
                FirstBlood: firstBlood,
                ShellId: -1);
    }
}
