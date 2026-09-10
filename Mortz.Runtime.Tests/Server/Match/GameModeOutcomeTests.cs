using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Modes;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class GameModeOutcomeTests
{
    private class ReachFinish : EndCondition
    {
        public override EndDecision? Evaluate(GameModeContext context, IReadOnlyList<ContenderScore> scores)
        {
            foreach (PlayerState player in context.World.Players.Values)
            {
                if (player.Health > 0 && player.Position.X >= 80)
                    return new EndDecision(EndReason.OBJECTIVE, new Victor.Player(player.PeerId));
            }
            return null;
        }
    }

    [Fact]
    public void TimedModeCountsEveryFinalTickDeathAndDoesNotReplayAnUnrelatedElimination()
    {
        using Fixture fixture = new(TimedRules());
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);

        fixture.AdvanceToDeadline();
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(3, byte.MaxValue);
        MatchUpdate update = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(2), update.MatchEnded);
        Assert.Equal(2, update.Deaths.Length);
        Assert.Equal(2, update.Eliminations.Length);
        Assert.Equal(1, fixture.Runtime.ScoreOf(fixture.Players[0]).Deaths);
        Assert.Equal(2, fixture.Runtime.ScoreOf(fixture.Players[1]).Deaths);
        Assert.Equal(1, fixture.Runtime.ScoreOf(fixture.Players[2]).Deaths);
        Assert.Null(update.FinalKill);
        Assert.Null(fixture.Runtime.FinalKill);
        Assert.Equal(MatchStage.VICTORY_LAP, fixture.Runtime.Stage);
    }

    [Fact]
    public void TimedModeDoesNotReplayEvenWhenTheWinningPlayerDiesOnTheDeadline()
    {
        using Fixture fixture = new(TimedRules());
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.Advance(default);
        fixture.Runtime.Advance(default);
        fixture.AdvanceToDeadline();
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate update = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(1), update.MatchEnded);
        Assert.Equal(2, update.Eliminations.Length);
        Assert.Null(update.FinalKill);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaceFinishesFromPositionWithNoReplayWhetherOrNotSomeoneDies(bool otherDeath)
    {
        using Fixture fixture = new(new ModeRules(), new GameMode(new KillScores(), new ReachFinish(),
            new QualifyingWinner(), EvaluationTiming.TICK, new NoReplay()));
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);
        fixture.Runtime.World.Teleport(1, new Vec2(81, 96));
        if (otherDeath)
            fixture.Runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate update = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(1), update.MatchEnded);
        Assert.Equal(otherDeath ? 1 : 0, update.Deaths.Length);
        Assert.Null(update.FinalKill);
        Assert.Null(fixture.Runtime.FinalKill);
    }

    [Theory]
    [InlineData("score_target")]
    [InlineData("score_lead")]
    public void TomlComposesExistingEndConditionsWithDeaths(string condition)
    {
        ModeRules rules = AuthoredRules($$"""
            [rules]
            score = "deaths"
            evaluation = "elimination"
            replay = "decisive_elimination"
            [rules.end_condition]
            type = "{{condition}}"
            target = 2
            """);
        using Fixture fixture = new(rules);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);

        MatchUpdate ended = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(1), ended.MatchEnded);
        Assert.Null(ended.FinalKill);
        Assert.Equal(-1, ended.Standing.Remaining);
    }

    [Fact]
    public void ChangingOnlyTheWinnerComponentSelectsTheFewestDeaths()
    {
        ModeRules rules = TimedRules();
        rules.Winner = WinnerRule.LOWEST_SCORE;
        using Fixture fixture = new(AuthoredRules(TomlModel.Write(new RulesetManifest { Rules = rules })));
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        fixture.AdvanceToDeadline();

        MatchUpdate ended = fixture.Runtime.Advance(default);
        Assert.Equal(new Victor.Player(3), ended.MatchEnded);
        Assert.Equal(ended.MatchEnded, ended.Standing.Leader);
    }

    [Theory]
    [InlineData("elimination", 1, true)]
    [InlineData("tick", 2, false)]
    public void TomlTimingChoosesTheScoringBoundary(string timing, int scoredDeaths, bool endsImmediately)
    {
        ModeRules rules = AuthoredRules($$"""
            [rules]
            score = "deaths"
            evaluation = "{{timing}}"
            replay = "none"
            [rules.end_condition]
            type = "score_target"
            target = 1
            """);
        using Fixture fixture = new(rules);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate update = fixture.Runtime.Advance(default);

        Assert.Equal(scoredDeaths, update.Eliminations.Length);
        Assert.Equal(endsImmediately, update.MatchEnded != null);
        if (!endsImmediately)
        {
            fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
            Assert.Equal(new Victor.Player(1), fixture.Runtime.Advance(default).MatchEnded);
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("decisive_elimination")]
    public void ATimedTieContinuesUntilThereIsAUniqueWinnerWithoutReplay(string replay)
    {
        ModeRules rules = TimedRules();
        rules.Replay = replay == "none" ? ReplayRule.NONE : ReplayRule.DECISIVE_ELIMINATION;
        using Fixture fixture = new(rules);
        fixture.AdvanceToDeadline();
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate ended = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(2), ended.MatchEnded);
        Assert.Null(ended.FinalKill);
    }

    [Fact]
    public void TeamDeathTotalsSurviveAPlayerLeaving()
    {
        ModeRules rules = TimedRules();
        rules.Teams = true;
        using Fixture fixture = new(rules);
        Team winningTeam = fixture.Runtime.World.Players[1].Team!.Value;
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.Advance(default);
        fixture.Runtime.Remove(fixture.Players[0]);
        fixture.AdvanceToDeadline();

        Assert.Equal(new Victor.Team(winningTeam), fixture.Runtime.Advance(default).MatchEnded);
    }

    [Fact]
    public void AnEmptyTeamCannotWinWithZeroDeaths()
    {
        ModeRules rules = TimedRules();
        rules.Teams = true;
        rules.Winner = WinnerRule.LOWEST_SCORE;
        using Fixture fixture = new(rules);
        Team remainingTeam = fixture.Runtime.World.Players[1].Team!.Value;
        fixture.Runtime.Remove(fixture.Players[1]);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.AdvanceToDeadline();

        Assert.Equal(new Victor.Team(remainingTeam), fixture.Runtime.Advance(default).MatchEnded);
    }

    private static ModeRules TimedRules() => AuthoredRules("""
        [rules]
        score = "deaths"
        winner = "highest_score"
        evaluation = "tick"
        replay = "none"
        [rules.end_condition]
        type = "time_limit"
        seconds = 1
        """);

    private static ModeRules AuthoredRules(string text)
    {
        ContentReadResult<GameModeManifest> read = TomlModel.Read<GameModeManifest>(
            "format_version = 1\nname = \"Composed fixture\"\n" + text);
        Assert.Empty(read.Diagnostics);
        GameModeManifest mode = Assert.IsType<GameModeManifest>(read.Value);
        MatchConfigSnapshot snapshot = mode.ToMatchConfigSnapshot();
        MatchConfig decoded = MatchConfigCodec.FromBytes(snapshot.ToBytes());
        Assert.Equal(snapshot, decoded.ToSnapshot());
        Assert.True(mode.Matches(decoded));
        return decoded.Rules;
    }

    private class Fixture : IDisposable
    {
        public MatchRuntime Runtime { get; }
        public Player[] Players { get; }

        public Fixture(ModeRules rules, GameMode? mode = null)
        {
            rules.SpawnImmunity = 0;
            rules.Respawn = new FixedRespawnRules { Delay = 0 };
            MatchStateKeys keys = new(1);
            Runtime = new MatchRuntime(new TerrainMask(128, 128, (_, y) => y >= 96, (_, _) => false),
                new MatchConfig { Rules = rules },
                2, keys, [new SpawnPoint(new Vec2(8, 96)), new SpawnPoint(new Vec2(24, 96)),
                    new SpawnPoint(new Vec2(40, 96))], mode: mode);
            keys.Seal();
            Players = Enumerable.Range(1, 3).Select(id => new Player(id, $"Player {id}", 0, 1)).ToArray();
            foreach (Player player in Players)
            {
                player.OpenMatch(keys.Count, keys.Generation);
                Runtime.Seat(player);
            }
        }

        public void AdvanceToDeadline()
        {
            while (Runtime.World.Tick < SimConfig.TICK_RATE - 1)
            {
                Assert.Null(Runtime.Advance(default).MatchEnded);
            }
        }

        public void Dispose()
        {
            foreach (Player player in Players)
            {
                player.CloseMatch();
            }
            Runtime.Dispose();
        }
    }
}
