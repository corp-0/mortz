using Mortz.Core.Match;
using Mortz.Server.Lobby;
using Xunit;

namespace Mortz.Tests.Server;

public class LobbySessionTests
{
    private static string Name(long peerId) => $"Player {peerId}";

    private static LobbySession Lobby(bool teams, params long[] peers) =>
        LobbySession.For(new SortedDictionary<long, string>(
            peers.ToDictionary(peer => peer, Name)), teams);

    [Fact]
    public void LobbyStartsOnlyWhenEveryConnectedPlayerIsReady()
    {
        LobbySession lobby = Lobby(teams: false, 3, 1);

        Assert.False(lobby.CanStart);
        Assert.True(lobby.SetReady(1, true));
        Assert.False(lobby.CanStart);
        Assert.True(lobby.SetReady(3, true));

        Assert.True(lobby.CanStart);
        Assert.Equal([1L, 3L], lobby.Players.Select(player => player.PeerId));
    }

    [Fact]
    public void RemovingTheOnlyUnreadyPlayerCanStartTheLobby()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);
        lobby.SetReady(1, true);

        lobby.Remove(2);

        Assert.True(lobby.CanStart);
    }

    [Fact]
    public void EnablingTeamsDealsEveryoneOutBalanced()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2, 3);

        Assert.True(lobby.SetTeamsEnabled(true));

        Assert.Equal<Team?>([Team.BLUE, Team.RED, Team.BLUE],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void TeamToggleWithoutTransitionChangesNothing()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);

        Assert.False(lobby.SetTeamsEnabled(false));
        Assert.True(lobby.SetTeamsEnabled(true));
        Assert.False(lobby.SetTeamsEnabled(true));
    }

    [Fact]
    public void JoinersLandOnTheSmallestTeamAndLeaversReshuffleNobody()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3);

        lobby.Remove(1); // blue loses a member, leaving 3 alone on it
        lobby.Add(4, Name(4));    // ties break to blue, so it fills back up
        lobby.Add(5, Name(5));    // blue now outnumbers, so 5 lands on red

        Assert.Equal<Team?>([Team.RED, Team.BLUE, Team.BLUE, Team.RED],
            lobby.Players.Select(player => player.Team));
        Assert.Equal([2L, 3L, 4L, 5L], lobby.Players.Select(player => player.PeerId));
    }

    [Fact]
    public void DisablingTeamsClearsAssignmentsAndReenablingDealsFresh()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);

        Assert.True(lobby.SetTeamsEnabled(false));
        Assert.All(lobby.Players, player => Assert.Null(player.Team));

        Assert.True(lobby.SetTeamsEnabled(true));
        Assert.Equal<Team?>([Team.BLUE, Team.RED],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void ReadyStatePersistsThroughTeamToggles()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);
        lobby.SetReady(1, true);

        lobby.SetTeamsEnabled(true);
        lobby.SetTeamsEnabled(false);

        Assert.Equal([true, false], lobby.Players.Select(player => player.Ready));
    }

    [Fact]
    public void PlayersJumpOnlyToTeamsWithAFreeSlot()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3);
        // Teams start blue/red/blue; capacity is 2 per side.

        Assert.True(lobby.TrySetTeam(1, Team.RED));  // red had a free slot
        Assert.False(lobby.TrySetTeam(3, Team.RED)); // now it is full
        Assert.False(lobby.TrySetTeam(3, Team.BLUE)); // already there
        Assert.False(lobby.TrySetTeam(99, Team.RED));

        Assert.Equal<Team?>([Team.RED, Team.RED, Team.BLUE],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void TeamJumpsNeedTeamsEnabled()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);

        Assert.False(lobby.TrySetTeam(1, Team.RED));
    }

    [Fact]
    public void MutualSwapOffersTradeTeamsAndKeepReadyState()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2); // 1/2
        lobby.SetReady(1, true);

        Assert.Equal(SwapResult.OFFERED, lobby.RequestSwap(1, 2));
        Assert.Equal([new SwapOffer(1, 2)], lobby.SwapOffers);
        Assert.Equal(SwapResult.SWAPPED, lobby.RequestSwap(2, 1));

        Assert.Equal<Team?>([Team.RED, Team.BLUE],
            lobby.Players.Select(player => player.Team));
        Assert.Equal([true, false], lobby.Players.Select(player => player.Ready));
        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void RepeatingAnOfferCancelsIt()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);

        Assert.Equal(SwapResult.OFFERED, lobby.RequestSwap(1, 2));
        Assert.Equal(SwapResult.CANCELLED, lobby.RequestSwap(1, 2));

        Assert.Empty(lobby.SwapOffers);
        Assert.Equal<Team?>([Team.BLUE, Team.RED],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void SwapOffersNeedACrossTeamPair()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1

        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(1, 3)); // same team
        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(1, 1));
        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(1, 99));

        LobbySession teamless = Lobby(teams: false, 1, 2);
        Assert.Equal(SwapResult.NONE, teamless.RequestSwap(1, 2));
    }

    [Fact]
    public void OffersDieWhenTheirPairStopsSpanningTeams()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1
        lobby.RequestSwap(1, 2);
        lobby.RequestSwap(3, 2);

        Assert.True(lobby.TrySetTeam(1, Team.RED)); // 1 joins 2's team, that offer is moot
        Assert.Equal([new SwapOffer(3, 2)], lobby.SwapOffers);

        lobby.Remove(2);
        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void TeamToggleWipesAllOffers()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);
        lobby.RequestSwap(1, 2);

        lobby.SetTeamsEnabled(false);
        lobby.SetTeamsEnabled(true);

        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void EmptyLobbyTeamToggleNeedsNoBroadcast()
    {
        LobbySession lobby = new();

        Assert.False(lobby.SetTeamsEnabled(true));

        lobby.Add(1, Name(1));
        Assert.Equal<Team?>(Team.BLUE, lobby.Players[0].Team);
    }
}
