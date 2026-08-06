using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Lobby;
using Xunit;

namespace Mortz.Tests.Server.Lobby;

public class LobbySessionTests
{
    private readonly LobbyCells _cells = new();

    private LobbySession Lobby(bool teams, params int[] peers)
    {
        LobbySession lobby = new(_cells.Keys, teams);
        foreach (int peer in peers)
        {
            lobby.Add(_cells.GetOrJoin(peer));
        }
        return lobby;
    }

    [Fact]
    public void LobbyStartsOnlyWhenEveryConnectedPlayerIsReady()
    {
        LobbySession lobby = Lobby(teams: false, 3, 1);

        Assert.False(lobby.CanStart);
        Assert.True(lobby.SetReady(_cells.GetOrJoin(1), true));
        Assert.False(lobby.CanStart);
        Assert.True(lobby.SetReady(_cells.GetOrJoin(3), true));

        Assert.True(lobby.CanStart);
        Assert.Equal([1, 3], lobby.Players.Select(player => player.PeerId));
    }

    [Fact]
    public void RemovingTheOnlyUnreadyPlayerCanStartTheLobby()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);
        lobby.SetReady(_cells.GetOrJoin(1), true);

        lobby.Remove(_cells.GetOrJoin(2));

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

        lobby.Remove(_cells.GetOrJoin(1)); // blue loses a member, leaving 3 alone on it
        lobby.Add(_cells.GetOrJoin(4));    // ties break to blue, so it fills back up
        lobby.Add(_cells.GetOrJoin(5));    // blue now outnumbers, so 5 lands on red

        Assert.Equal<Team?>([Team.RED, Team.BLUE, Team.BLUE, Team.RED],
            lobby.Players.Select(player => player.Team));
        Assert.Equal([2, 3, 4, 5], lobby.Players.Select(player => player.PeerId));
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
        lobby.SetReady(_cells.GetOrJoin(1), true);

        lobby.SetTeamsEnabled(true);
        lobby.SetTeamsEnabled(false);

        Assert.Equal([true, false], lobby.Players.Select(player => player.Ready));
    }

    [Fact]
    public void PlayersJumpOnlyToTeamsWithAFreeSlot()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3);
        // Teams start blue/red/blue; capacity is 2 per side.

        Assert.True(lobby.TrySetTeam(_cells.GetOrJoin(1), Team.RED));  // red had a free slot
        Assert.False(lobby.TrySetTeam(_cells.GetOrJoin(3), Team.RED)); // now it is full
        Assert.False(lobby.TrySetTeam(_cells.GetOrJoin(3), Team.BLUE)); // already there
        Assert.False(lobby.TrySetTeam(_cells.GetOrJoin(99), Team.RED)); // never seated

        Assert.Equal<Team?>([Team.RED, Team.RED, Team.BLUE],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void TeamJumpsNeedTeamsEnabled()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);

        Assert.False(lobby.TrySetTeam(_cells.GetOrJoin(1), Team.RED));
    }

    [Fact]
    public void MutualSwapOffersTradeTeamsAndKeepReadyState()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2); // 1/2
        lobby.SetReady(_cells.GetOrJoin(1), true);

        Assert.Equal(SwapResult.OFFERED, lobby.RequestSwap(_cells.GetOrJoin(1), 2));
        Assert.Equal([new SwapOffer(1, 2)], lobby.SwapOffers);
        Assert.Equal(SwapResult.SWAPPED, lobby.RequestSwap(_cells.GetOrJoin(2), 1));

        Assert.Equal<Team?>([Team.RED, Team.BLUE],
            lobby.Players.Select(player => player.Team));
        Assert.Equal([true, false], lobby.Players.Select(player => player.Ready));
        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void RepeatingAnOfferCancelsIt()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);

        Assert.Equal(SwapResult.OFFERED, lobby.RequestSwap(_cells.GetOrJoin(1), 2));
        Assert.Equal(SwapResult.CANCELLED, lobby.RequestSwap(_cells.GetOrJoin(1), 2));

        Assert.Empty(lobby.SwapOffers);
        Assert.Equal<Team?>([Team.BLUE, Team.RED],
            lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void SwapOffersNeedACrossTeamPair()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1

        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(_cells.GetOrJoin(1), 3)); // same team
        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(_cells.GetOrJoin(1), 1));
        Assert.Equal(SwapResult.NONE, lobby.RequestSwap(_cells.GetOrJoin(1), 99));

        LobbyCells teamlessCells = new();
        LobbySession teamless = new(teamlessCells.Keys, teamsEnabled: false);
        teamless.Add(teamlessCells.GetOrJoin(1));
        teamless.Add(teamlessCells.GetOrJoin(2));
        Assert.Equal(SwapResult.NONE, teamless.RequestSwap(teamlessCells.GetOrJoin(1), 2));
    }

    [Fact]
    public void OffersDieWhenTheirPairStopsSpanningTeams()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1
        lobby.RequestSwap(_cells.GetOrJoin(1), 2);
        lobby.RequestSwap(_cells.GetOrJoin(3), 2);

        // 1 joins 2's team, that offer is moot
        Assert.True(lobby.TrySetTeam(_cells.GetOrJoin(1), Team.RED));
        Assert.Equal([new SwapOffer(3, 2)], lobby.SwapOffers);

        lobby.Remove(_cells.GetOrJoin(2));
        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void TeamToggleWipesAllOffers()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);
        lobby.RequestSwap(_cells.GetOrJoin(1), 2);

        lobby.SetTeamsEnabled(false);
        lobby.SetTeamsEnabled(true);

        Assert.Empty(lobby.SwapOffers);
    }

    [Fact]
    public void EmptyLobbyTeamToggleNeedsNoBroadcast()
    {
        LobbySession lobby = new(_cells.Keys, teamsEnabled: false);

        Assert.False(lobby.SetTeamsEnabled(true));

        lobby.Add(_cells.GetOrJoin(1));
        Assert.Equal<Team?>(Team.BLUE, lobby.Players[0].Team);
    }

    [Fact]
    public void ALeaverIsScrubbedFromOtherPlayersOfferCells()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3, 4); // blue/red/blue/red
        lobby.RequestSwap(_cells.GetOrJoin(1), 2); // 1 targets the leaver
        lobby.RequestSwap(_cells.GetOrJoin(2), 3); // the leaver's own outgoing offer

        Assert.True(lobby.Remove(_cells.GetOrJoin(2)));

        // Neither the offer at the leaver nor the leaver's own offer survive.
        Assert.Empty(lobby.SwapOffers);
        // 1's cell no longer holds the leaver: a fresh offer starts clean
        // instead of cancelling against the stale target.
        Assert.Equal(SwapResult.OFFERED, lobby.RequestSwap(_cells.GetOrJoin(1), 4));
    }
}
