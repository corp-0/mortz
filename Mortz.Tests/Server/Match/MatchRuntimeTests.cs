using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Mortz.Server.Services;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class MatchRuntimeTests
{
    private const int GENERATION = 4;

    [Fact]
    public void ConstructionClaimsEverySystemKeyBeforePlayersOpenMatchState()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys);
        Player player = OpenPlayer(1, keys);

        runtime.Seat(player);

        Assert.Equal(3, keys.Count);
        Assert.Equal(MatchParticipation.Active, runtime.ParticipationOf(player));
        Assert.Equal(0, runtime.ScoreOf(player).Kills);
    }

    [Fact]
    public void AdvanceRunsTheFullDependencyOrderedPipeline()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys);
        Player victim = OpenPlayer(1, keys);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);

        MatchUpdate update = runtime.Advance(default);

        Assert.Equal(1, update.Tick);
        Assert.Single(update.Deaths);
        Assert.Single(update.Eliminations);
        Assert.Single(update.ParticipationChanges);
        Assert.NotEmpty(update.GameEvents);
    }

    [Fact]
    public void AuthoredSpawnPointsReachTheWorld()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys,
            spawnPoints: [new Vec2(32, 64), new Vec2(96, 64)]);

        runtime.Seat(OpenPlayer(50, keys));
        runtime.Seat(OpenPlayer(10, keys));

        Assert.Equal(new Vec2(32, 64), runtime.World.Players[50].Position);
        Assert.Equal(new Vec2(96, 64), runtime.World.Players[10].Position);
    }

    [Fact]
    public void LobbyTeamsCarryIntoTheMatchAndLateJoinersBalance()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys, teams: true);

        runtime.Seat(OpenPlayer(1, keys), Team.RED);
        runtime.Seat(OpenPlayer(2, keys), Team.RED);
        runtime.Seat(OpenPlayer(3, keys), Team.BLUE);

        Team? lateJoiner = runtime.Seat(OpenPlayer(4, keys));

        Assert.Equal(Team.RED, runtime.World.Players[1].Team);
        Assert.Equal(Team.RED, runtime.World.Players[2].Team);
        Assert.Equal(Team.BLUE, runtime.World.Players[3].Team);
        Assert.Equal(Team.BLUE, lateJoiner);
    }

    [Fact]
    public void LobbyTeamsAreIgnoredWhenTeamsAreOff()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys);

        Assert.Null(runtime.Seat(OpenPlayer(1, keys), Team.RED));
    }

    [Fact]
    public void SeatingSpectatingAndRemovalAreOwnedByTheRuntime()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys);
        Player seated = OpenPlayer(1, keys);
        Player spectator = OpenPlayer(2, keys);

        runtime.Seat(seated);
        runtime.AddJipSpectator(spectator);

        Assert.Contains(seated.PeerId, runtime.World.Players.Keys);
        Assert.DoesNotContain(spectator.PeerId, runtime.World.Players.Keys);
        Assert.Equal(MatchParticipation.JipSpectator, runtime.ParticipationOf(spectator));
        Assert.Equal([seated.PeerId],
            runtime.ScoreRows().Select(row => row.Player.PeerId));

        runtime.Remove(seated);

        Assert.DoesNotContain(seated.PeerId, runtime.World.Players.Keys);
        Assert.Empty(runtime.ScoreRows());
    }

    [Fact]
    public void WinnersIncludeOnlyPlayersStillSeated()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys, teams: true);
        Player blue = OpenPlayer(1, keys);
        Player red = OpenPlayer(2, keys);
        Player departedBlue = OpenPlayer(3, keys);
        runtime.Seat(blue, Team.BLUE);
        runtime.Seat(red, Team.RED);
        runtime.Seat(departedBlue, Team.BLUE);
        runtime.Remove(departedBlue);

        Assert.Equal([1],
            runtime.Winners(new Victor.Team(Team.BLUE)).Select(player => player.PeerId));
        Assert.Equal([2],
            runtime.Winners(new Victor.Player(2)).Select(player => player.PeerId));
    }

    [Fact]
    public void WinningDeathStopsLaterScoringButParticipationSeesEveryDeath()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys,
            killTarget: 1,
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY,
            spawnPoints: [new Vec2(10, 32), new Vec2(40, 32), new Vec2(120, 32)]);
        Player first = OpenPlayer(1, keys);
        Player second = OpenPlayer(2, keys);
        Player rewarded = OpenPlayer(3, keys);
        runtime.Seat(first);
        runtime.Seat(second);
        runtime.Seat(rewarded);
        runtime.World.QueueDamage(first.PeerId, byte.MaxValue);
        runtime.World.QueueDamage(second.PeerId, byte.MaxValue);

        MatchUpdate update = runtime.Advance(default);

        Assert.Equal([1, 2], update.Deaths.Select(death => death.PeerId));
        ScoredKill scored = Assert.Single(update.Eliminations);
        Assert.Equal(1, scored.Score.VictimId);
        Assert.Equal(new KillReward(3, 1), scored.Score.Reward);
        Assert.Equal(new Victor.Player(3), update.MatchEnded);
        Assert.Equal(1, runtime.ScoreOf(first).Deaths);
        Assert.Equal(0, runtime.ScoreOf(second).Deaths);
        Assert.Equal([1, 2], update.ParticipationChanges.Select(change => change.PeerId));
    }

    [Fact]
    public void VictoryLapFreezesTheWorldRejectsInputsAndUsesItsOwnCountdown()
    {
        MatchStateKeys keys = new(GENERATION);
        using MatchRuntime runtime = NewRuntime(keys,
            killTarget: 1,
            victoryLapTicks: 3,
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY,
            spawnPoints: [new Vec2(10, 32), new Vec2(40, 32)]);
        Player winner = OpenPlayer(1, keys);
        Player victim = OpenPlayer(2, keys);
        runtime.Seat(winner);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);

        MatchUpdate winningUpdate = runtime.Advance(default);

        Assert.Equal(new Victor.Player(winner.PeerId), winningUpdate.MatchEnded);
        Assert.Equal(MatchStage.VICTORY_LAP, runtime.Stage);
        int frozenTick = runtime.World.Tick;
        runtime.EnqueueInput(winner, 0, new PlayerInput(InputButtons.RIGHT));
        Assert.Equal(0, runtime.World.PendingInputs(winner.PeerId));

        MatchUpdate first = runtime.Advance(default);
        MatchUpdate second = runtime.Advance(default);
        MatchUpdate third = runtime.Advance(default);

        Assert.Equal(frozenTick, runtime.World.Tick);
        Assert.False(first.ReturnToLobby);
        Assert.False(second.ReturnToLobby);
        Assert.True(third.ReturnToLobby);
        Assert.Empty(first.MortarEvents);
        Assert.Empty(first.Explosions);
    }

    [Fact]
    public void DisposalIsIdempotentAndClosesTheRuntime()
    {
        MatchRuntime runtime = NewRuntime(new MatchStateKeys(GENERATION));

        runtime.Dispose();
        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runtime.Advance(default));
    }

    [Fact]
    public void RuntimeDoesNotParticipateInTheHostAdvanceContract()
    {
        using MatchRuntime runtime = NewRuntime(new MatchStateKeys(GENERATION));

        Assert.IsNotAssignableFrom<IAdvance>(runtime);
    }

    private static MatchRuntime NewRuntime(
        MatchStateKeys keys,
        bool teams = false,
        int killTarget = 20,
        int victoryLapTicks = 3,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE,
        IReadOnlyList<Vec2>? spawnPoints = null)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchRuntime(terrain, new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = teams,
                Victory = new KillsVictoryRules { Target = killTarget },
                SuicidePenalty = suicidePenalty,
                SpawnImmunity = 0,
            },
        }, victoryLapTicks, keys,
            spawnPoints?.Select(point => new SpawnPoint(point)).ToArray());
    }

    private static Player OpenPlayer(int peerId, MatchStateKeys keys)
    {
        Player player = new(peerId, $"Player {peerId}", serverKeyCount: 0,
            serverGeneration: GENERATION);
        player.OpenMatch(keys.Count, keys.Generation);
        return player;
    }
}
