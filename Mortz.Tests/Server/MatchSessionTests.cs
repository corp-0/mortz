using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Server;

public class MatchSessionTests
{
    private readonly MatchStateKeys _keys = new(1);
    private readonly Dictionary<int, Player> _players = new();

    /// <summary>Mint after the session claimed its keys; same id, same player.</summary>
    private Player P(int peerId)
    {
        if (_players.TryGetValue(peerId, out Player? present))
            return present;
        Player player = new(peerId, $"Player {peerId}", serverKeyCount: 0,
            serverGeneration: 1);
        player.OpenMatch(_keys.Count, _keys.Generation);
        _players[peerId] = player;
        return player;
    }

    private MatchSession Session(bool teams = false, int killTarget = 20,
        int victoryLapTicks = 10, IReadOnlyList<Vec2>? spawnPoints = null,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE, float respawnDelay = 2)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchSession(terrain, new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = teams,
                KillTarget = killTarget,
                SuicidePenalty = suicidePenalty,
                SpawnImmunity = 0,
                RespawnDelay = respawnDelay,
            },
        }, victoryLapTicks, _keys,
            spawnPoints?.Select(point => new SpawnPoint(point)).ToArray());
    }

    [Fact]
    public void AuthoredSpawnPoints_ReachTheWorld()
    {
        MatchSession match = Session(spawnPoints: [new Vec2(32, 64), new Vec2(96, 64)]);

        match.AddPlayer(P(50));
        match.AddPlayer(P(10));

        Assert.Equal(new Vec2(32, 64), match.World.Players[50].Position);
        Assert.Equal(new Vec2(96, 64), match.World.Players[10].Position);
    }

    [Fact]
    public void UncreditedDeathDoesNotConsumeGlobalFirstBlood()
    {
        MatchSession match = Session();
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));

        ScoredKill uncredited = match.ScoreDeath(new Death(2, default, 99, false, ShellId: -1))!.Value;
        ScoredKill credited = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;

        Assert.False(uncredited.FirstBlood);
        Assert.True(credited.FirstBlood);
    }

    [Fact]
    public void FirstBloodIsClaimedOnceAcrossAllPlayers()
    {
        MatchSession match = Session();
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));
        match.AddPlayer(P(3));

        ScoredKill first = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;
        ScoredKill second = match.ScoreDeath(new Death(2, default, 3, false, ShellId: -1))!.Value;

        Assert.True(first.FirstBlood);
        Assert.False(second.FirstBlood);
    }

    [Fact]
    public void TeamKillDoesNotConsumeFirstBlood()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(P(1)); // blue
        match.AddPlayer(P(2)); // red
        match.AddPlayer(P(3)); // blue

        ScoredKill teamKill = match.ScoreDeath(new Death(3, default, 1, false, ShellId: -1))!.Value;
        ScoredKill credited = match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1))!.Value;

        Assert.Equal(DeathKind.TEAM_KILL, teamKill.Score.Kind);
        Assert.False(teamKill.FirstBlood);
        Assert.True(credited.FirstBlood);
    }

    [Fact]
    public void LobbyTeamsCarryIntoTheMatchAndLateJoinersBalance()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(P(1), lobbyTeam: Team.RED);
        match.AddPlayer(P(2), lobbyTeam: Team.RED);
        match.AddPlayer(P(3), lobbyTeam: Team.BLUE);

        Team? lateJoiner = match.AddPlayer(P(4)); // no lobby team: smallest wins

        Assert.Equal(Team.RED, match.World.Players[1].Team);
        Assert.Equal(Team.RED, match.World.Players[2].Team);
        Assert.Equal(Team.BLUE, match.World.Players[3].Team);
        Assert.Equal(Team.BLUE, lateJoiner);
    }

    [Fact]
    public void LobbyTeamsAreIgnoredWhenTeamsAreOff()
    {
        MatchSession match = Session(teams: false);

        Assert.Null(match.AddPlayer(P(1), lobbyTeam: Team.RED));
    }

    [Fact]
    public void SpectatorsHaveParticipationWithoutSimulationOrScoreState()
    {
        MatchSession match = Session();

        match.AddJipSpectator(P(7));

        Assert.Equal(MatchParticipation.JipSpectator, match.ParticipationOf(P(7)));
        Assert.DoesNotContain(7, match.World.Players.Keys);
        Assert.DoesNotContain(match.Scores.Rows(), row => row.Player.PeerId == 7);
    }

    [Fact]
    public void DeathHoldsTheCameraForTheDeathViewBeforeSpectatingAndReturnsToActive()
    {
        MatchSession match = Session(respawnDelay: 6);
        match.AddPlayer(P(1));
        match.World.QueueDamage(1, byte.MaxValue);

        MatchFrame death = match.Step();

        MatchParticipationChange presentation = Assert.Single(death.ParticipationChanges);
        Assert.Equal(MatchActivity.DEATH_PRESENTATION, presentation.State.Activity);

        MatchParticipationChange[] beforeHold = Enumerable.Range(
                0, MatchSession.DEATH_VIEW_DURATION_TICKS - 1)
            .SelectMany(_ => match.Step().ParticipationChanges)
            .ToArray();
        Assert.DoesNotContain(beforeHold,
            change => change.State.Activity == MatchActivity.SPECTATING);

        MatchParticipationChange spectating = Assert.Single(match.Step().ParticipationChanges);
        Assert.Equal(MatchActivity.SPECTATING, spectating.State.Activity);
        Assert.Equal(SpectateReason.RESPAWN, spectating.State.Reason);

        MatchParticipationChange? active = null;
        while (match.World.Players[1].RespawnTicks > 0)
        {
            MatchFrame frame = match.Step();
            foreach (MatchParticipationChange change in frame.ParticipationChanges)
            {
                if (change.State.Activity == MatchActivity.ACTIVE)
                    active = change;
            }
        }
        Assert.NotNull(active);
        Assert.Equal(MatchParticipation.Active, active.Value.State);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void RespawnWithoutThreeSecondsToSpectateNeverEntersSpectator(float respawnDelay)
    {
        MatchSession match = Session(respawnDelay: respawnDelay);
        match.AddPlayer(P(1));
        match.World.QueueDamage(1, byte.MaxValue);

        MatchFrame death = match.Step();
        List<MatchParticipationChange> changes = [.. death.ParticipationChanges];
        while (match.World.Players[1].RespawnTicks > 0)
            changes.AddRange(match.Step().ParticipationChanges);

        Assert.Contains(changes,
            change => change.State.Activity == MatchActivity.DEATH_PRESENTATION);
        Assert.DoesNotContain(changes,
            change => change.State.Activity == MatchActivity.SPECTATING);
        Assert.Contains(changes,
            change => change.State.Activity == MatchActivity.ACTIVE);
    }

    [Fact]
    public void SoloWinnerIsTheOnlyCreditedPeer()
    {
        MatchSession match = Session();
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));

        Assert.Equal([1], match.Winners(new Victor.Player(1)).Select(player => player.PeerId));
    }

    [Fact]
    public void TeamWinCreditsEveryTeammateStillInTheMatch()
    {
        MatchSession match = Session(teams: true);
        match.AddPlayer(P(1)); // blue
        match.AddPlayer(P(2)); // red
        match.AddPlayer(P(3)); // blue
        match.AddPlayer(P(4)); // red
        match.RemovePlayer(P(3));

        int[] winners = match.Winners(new Victor.Team(Team.BLUE)).Select(player => player.PeerId).ToArray();

        Assert.Equal([1], winners);
        Assert.Equal([2, 4], match.Winners(new Victor.Team(Team.RED)).Select(player => player.PeerId).Order());
    }

    [Fact]
    public void MatchPointEntersAtOneRemaining_LeavesWhenTheLeadDrops_AnnouncesOnce()
    {
        MatchSession match = Session(killTarget: 2, suicidePenalty: SuicidePenalty.KILL);
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));

        match.ScoreDeath(new Death(2, default, 1, false, ShellId: -1));
        MatchFrame enter = match.Step();
        MatchFrame steady = match.Step();
        match.ScoreDeath(new Death(1, default, 1, false, ShellId: -1)); // suicide penalty
        MatchFrame leave = match.Step();

        Assert.Equal(new MatchPointChange(new MatchPoint(1, new Victor.Player(1))),
            enter.MatchPoint);
        Assert.Null(steady.MatchPoint);
        // The penalty wiped the lead: nobody has scored, so there is no leader.
        Assert.Equal(new MatchPointChange(null), leave.MatchPoint);
    }

    [Fact]
    public void RewardedSuicide_GrantsTheKillToTheNearestLivingEnemy()
    {
        MatchSession match = Session(
            spawnPoints: [new Vec2(10, 64), new Vec2(40, 64), new Vec2(120, 64)],
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));
        match.AddPlayer(P(3));

        ScoredKill scored =
            match.ScoreDeath(new Death(1, new Vec2(10, 64), 1, false, ShellId: -1))!.Value;

        Assert.Equal(new KillReward(2, 1), scored.Score.Reward);
        Assert.Equal(0, match.Scores.Of(P(1)).Kills);
        Assert.Equal(1, match.Scores.Of(P(2)).Kills);
    }

    [Fact]
    public void RewardedSuicide_NeverPaysATeammate()
    {
        MatchSession match = Session(teams: true,
            spawnPoints: [new Vec2(10, 64), new Vec2(40, 64), new Vec2(120, 64)],
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY);
        match.AddPlayer(P(1), Team.BLUE);
        match.AddPlayer(P(2), Team.BLUE); // closest, but on the victim's team
        match.AddPlayer(P(3), Team.RED);

        ScoredKill scored =
            match.ScoreDeath(new Death(1, new Vec2(10, 64), 0, false, ShellId: -1))!.Value;

        Assert.Equal(new KillReward(3, 1), scored.Score.Reward);
        Assert.Equal(0, match.Scores.Of(P(2)).Kills);
    }

    [Fact]
    public void VictoryLapFreezesWorldRejectsInputsAndUsesSeparateCountdown()
    {
        MatchSession match = Session(killTarget: 1, victoryLapTicks: 3);
        match.AddPlayer(P(1));
        match.AddPlayer(P(2));

        ScoredKill winner = match.ScoreDeath(
            new Death(2, new Vec2(40, 50), 1, false, ShellId: -1))!.Value;
        Assert.NotNull(winner.Score.Winner);
        Assert.Equal(MatchStage.VICTORY_LAP, match.Stage);

        match.EnqueueInput(P(1), 0, new PlayerInput(InputButtons.RIGHT));
        Assert.Equal(0, match.World.PendingInputs(1));

        MatchFrame first = match.Step();
        MatchFrame second = match.Step();
        MatchFrame third = match.Step();

        Assert.Equal(0, match.World.Tick);
        Assert.False(first.ReturnToLobby);
        Assert.False(second.ReturnToLobby);
        Assert.True(third.ReturnToLobby);
        Assert.Empty(first.MortarEvents);
        Assert.Empty(first.Explosions);
    }
}
