using Godot;
using Mortz.Client.Match;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Match;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class ClientMatchStateTests : NodeServiceTest
{
    [Fact]
    public void InitialAndLiveParticipationUseTheSameStateProperty()
    {
        MatchParticipation initial = MatchParticipation.JipSpectator;
        ClientMatchState state = HostState(4, initial);

        new MatchParticipationMsg(
            MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1).Broadcast(Router);
        new MatchParticipationMsg(
            initial.Seat, initial.Activity, initial.Reason, initial.ReturnTick).Broadcast(Router);

        Assert.Equal(initial, state.Participation);
    }

    [Fact]
    public void InvalidParticipationIsIgnored()
    {
        ClientMatchState state = HostState(4, MatchParticipation.JipSpectator);
        int changes = 0;
        state.ParticipationChanged += _ => changes++;

        new MatchParticipationMsg(
            MatchSeat.SPECTATOR, MatchActivity.ACTIVE, SpectateReason.NONE, -1).Broadcast(Router);

        Assert.Equal(MatchParticipation.JipSpectator, state.Participation);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void StaleGenerationCannotMutateTheCurrentMatch()
    {
        ClientMatchState state = new(8, MatchParticipation.Active);
        int changes = 0;
        state.ParticipationChanged += _ => changes++;
        state.MatchPointChanged += _ => changes++;
        state.WinnerChanged += _ => changes++;
        state.ScoresChanged += _ => changes++;

        Assert.False(state.TryApplyParticipation(7, MatchParticipation.JipSpectator));
        Assert.False(state.TryApplyMatchPoint(7, new MatchPoint(1, null)));
        Assert.False(state.TryApplyWinner(7, new Victor.Player(2)));
        Assert.False(state.TryReplaceScores(7, [new MatchScoreRow(2, 9, 3)], default));
        Assert.False(state.TryPatchScores(7, new MatchScorePatch(
            2, 3, false, 1, 1, 0, 0, default)));

        Assert.Equal(MatchParticipation.Active, state.Participation);
        Assert.Null(state.MatchPoint);
        Assert.Null(state.Winner);
        Assert.Empty(state.Scores.Players);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ReconnectAndSecondMatchStartFresh()
    {
        ClientMatchState first = new(1, MatchParticipation.Active);
        first.TryApplyMatchPoint(1, new MatchPoint(2, new Victor.Player(7)));
        first.TryApplyWinner(1, new Victor.Player(7));
        first.TryReplaceScores(1, [new MatchScoreRow(7, 5, 1)], new TeamKills(5, 0));
        first.Close();

        ClientMatchState second = new(2, MatchParticipation.JipSpectator);

        Assert.False(first.TryApplyParticipation(1, MatchParticipation.JipSpectator));
        Assert.Equal(MatchParticipation.JipSpectator, second.Participation);
        Assert.Null(second.MatchPoint);
        Assert.Null(second.Winner);
        Assert.Empty(second.Scores.Players);
        Assert.Equal(default, second.Scores.TeamKills);
    }

    [Fact]
    public void MatchPointUsesTheSameStateBeforeAndAfterObserverSubscription()
    {
        ClientMatchState state = HostState(4, MatchParticipation.Active);
        MatchPoint expected = new(2, new Victor.Team(Team.RED));
        MatchProtocol.Encode(expected).Broadcast(Router);
        MatchPoint? observed = state.MatchPoint;
        state.MatchPointChanged += value => observed = value;

        MatchPoint replacement = new(1, new Victor.Player(7));
        MatchProtocol.Encode(replacement).Broadcast(Router);

        Assert.Equal(replacement, observed);
        Assert.Equal(replacement, state.MatchPoint);
    }

    [Fact]
    public void DetachedAdapterClosesTheMatchBeforeDeferredFree()
    {
        ClientMatchState state = new(4, MatchParticipation.Active);
        ClientMatchStateAdapter adapter = new();
        adapter.Initialize(state);
        HostRouted(adapter);

        ((SceneTree)Engine.GetMainLoop()).Root.RemoveChild(adapter);
        new MatchParticipationMsg(
            MatchSeat.SPECTATOR, MatchActivity.SPECTATING, SpectateReason.JIP, -1).Broadcast(Router);

        Assert.Equal(MatchParticipation.Active, state.Participation);
    }

    private ClientMatchState HostState(int generation, MatchParticipation participation)
    {
        ClientMatchState state = new(generation, participation);
        ClientMatchStateAdapter adapter = new();
        adapter.Initialize(state);
        HostRouted(adapter);
        return state;
    }
}
