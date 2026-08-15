using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Lobby;
using Mortz.Server.Lobby;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server.Lobby;

public class LobbySessionTests
{
    private readonly SortedDictionary<int, Player> _players = [];

    private LobbySession Lobby(bool teams, params int[] peers)
    {
        LobbySession lobby = new(teams);
        foreach (int peer in peers)
        {
            Assert.NotNull(lobby.Join(Player(peer)));
        }
        return lobby;
    }

    [Fact]
    public void LobbyStartsOnlyWhenEveryConnectedPlayerIsReady()
    {
        LobbySession lobby = Lobby(teams: false, 3, 1);

        Assert.False(lobby.CanStart);
        Assert.NotNull(lobby.SetReady(Player(1), true));
        Assert.False(lobby.CanStart);
        Assert.NotNull(lobby.SetReady(Player(3), true));

        Assert.True(lobby.CanStart);
        Assert.Equal([1, 3], lobby.Snapshot.Members.Select(player => player.PeerId));
    }

    [Fact]
    public void RemovingTheOnlyUnreadyPlayerCanStartTheLobby()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);
        lobby.SetReady(Player(1), true);

        lobby.Leave(Player(2));

        Assert.True(lobby.CanStart);
    }

    [Fact]
    public void EnablingTeamsDealsEveryoneOutBalanced()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2, 3);

        Assert.NotNull(lobby.SetTeamsEnabled(true));

        Assert.Equal<Team?>([Team.BLUE, Team.RED, Team.BLUE],
            lobby.Snapshot.Members.Select(player => player.Team));
    }

    [Fact]
    public void TeamToggleWithoutTransitionChangesNothing()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);

        Assert.Null(lobby.SetTeamsEnabled(false));
        Assert.NotNull(lobby.SetTeamsEnabled(true));
        Assert.Null(lobby.SetTeamsEnabled(true));
    }

    [Fact]
    public void JoinersLandOnTheSmallestTeamAndLeaversReshuffleNobody()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3);

        lobby.Leave(Player(1)); // blue loses a member, leaving 3 alone on it
        lobby.Join(Player(4));  // ties break to blue, so it fills back up
        lobby.Join(Player(5));  // blue now outnumbers, so 5 lands on red

        Assert.Equal<Team?>([Team.RED, Team.BLUE, Team.BLUE, Team.RED],
            lobby.Snapshot.Members.Select(player => player.Team));
        Assert.Equal([2, 3, 4, 5], lobby.Snapshot.Members.Select(player => player.PeerId));
    }

    [Fact]
    public void DisablingTeamsClearsAssignmentsAndReenablingDealsFresh()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);

        Assert.NotNull(lobby.SetTeamsEnabled(false));
        Assert.All(lobby.Snapshot.Members, player => Assert.Null(player.Team));

        Assert.NotNull(lobby.SetTeamsEnabled(true));
        Assert.Equal<Team?>([Team.BLUE, Team.RED],
            lobby.Snapshot.Members.Select(player => player.Team));
    }

    [Fact]
    public void ReadyStatePersistsThroughTeamToggles()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);
        lobby.SetReady(Player(1), true);

        lobby.SetTeamsEnabled(true);
        lobby.SetTeamsEnabled(false);

        Assert.Equal([true, false], lobby.Snapshot.Members.Select(player => player.Ready));
    }

    [Fact]
    public void PlayersJumpOnlyToTeamsWithAFreeSlot()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3);
        // Teams start blue/red/blue; capacity is 2 per side.

        Assert.NotNull(lobby.TrySetTeam(Player(1), Team.RED));  // red had a free slot
        Assert.Null(lobby.TrySetTeam(Player(3), Team.RED)); // now it is full
        Assert.Null(lobby.TrySetTeam(Player(3), Team.BLUE)); // already there
        Assert.Null(lobby.TrySetTeam(Player(99), Team.RED)); // never seated

        Assert.Equal<Team?>([Team.RED, Team.RED, Team.BLUE],
            lobby.Snapshot.Members.Select(player => player.Team));
    }

    [Fact]
    public void TeamJumpsNeedTeamsEnabled()
    {
        LobbySession lobby = Lobby(teams: false, 1, 2);

        Assert.Null(lobby.TrySetTeam(Player(1), Team.RED));
    }

    [Fact]
    public void MutualSwapOffersTradeTeamsAndKeepReadyState()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2); // 1/2
        lobby.SetReady(Player(1), true);

        Assert.IsType<LobbyChange.SwapOffered>(lobby.RequestSwap(Player(1), 2)!.Change);
        Assert.Equal([new SwapOffer(1, 2)], lobby.Snapshot.Offers);
        Assert.IsType<LobbyChange.TeamsSwapped>(lobby.RequestSwap(Player(2), 1)!.Change);

        Assert.Equal<Team?>([Team.RED, Team.BLUE],
            lobby.Snapshot.Members.Select(player => player.Team));
        Assert.Equal([true, false], lobby.Snapshot.Members.Select(player => player.Ready));
        Assert.Empty(lobby.Snapshot.Offers);
    }

    [Fact]
    public void RepeatingAnOfferCancelsIt()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);

        Assert.IsType<LobbyChange.SwapOffered>(lobby.RequestSwap(Player(1), 2)!.Change);
        Assert.IsType<LobbyChange.SwapCancelled>(lobby.RequestSwap(Player(1), 2)!.Change);

        Assert.Empty(lobby.Snapshot.Offers);
        Assert.Equal<Team?>([Team.BLUE, Team.RED],
            lobby.Snapshot.Members.Select(player => player.Team));
    }

    [Fact]
    public void SwapOffersNeedACrossTeamPair()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1

        Assert.Null(lobby.RequestSwap(Player(1), 3)); // same team
        Assert.Null(lobby.RequestSwap(Player(1), 1));
        Assert.Null(lobby.RequestSwap(Player(1), 99));

        LobbySession teamless = new(teamsEnabled: false);
        teamless.Join(Player(1));
        teamless.Join(Player(2));
        Assert.Null(teamless.RequestSwap(Player(1), 2));
    }

    [Fact]
    public void OffersDieWhenTheirPairStopsSpanningTeams()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3); // 1/2/1
        lobby.RequestSwap(Player(1), 2);
        lobby.RequestSwap(Player(3), 2);

        // 1 joins 2's team, that offer is moot
        Assert.NotNull(lobby.TrySetTeam(Player(1), Team.RED));
        Assert.Equal([new SwapOffer(3, 2)], lobby.Snapshot.Offers);

        lobby.Leave(Player(2));
        Assert.Empty(lobby.Snapshot.Offers);
    }

    [Fact]
    public void TeamToggleWipesAllOffers()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2);
        lobby.RequestSwap(Player(1), 2);

        lobby.SetTeamsEnabled(false);
        lobby.SetTeamsEnabled(true);

        Assert.Empty(lobby.Snapshot.Offers);
    }

    [Fact]
    public void EmptyLobbyTeamToggleNeedsNoBroadcast()
    {
        LobbySession lobby = new(teamsEnabled: false);

        Assert.Null(lobby.SetTeamsEnabled(true));

        lobby.Join(Player(1));
        Assert.Equal<Team?>(Team.BLUE, lobby.Snapshot.Members[0].Team);
    }

    [Fact]
    public void ALeaverIsScrubbedFromOtherPlayersOfferCells()
    {
        LobbySession lobby = Lobby(teams: true, 1, 2, 3, 4); // blue/red/blue/red
        lobby.RequestSwap(Player(1), 2); // 1 targets the leaver
        lobby.RequestSwap(Player(2), 3); // the leaver's own outgoing offer

        Assert.NotNull(lobby.Leave(Player(2)));

        // Neither the offer at the leaver nor the leaver's own offer survive.
        Assert.Empty(lobby.Snapshot.Offers);
        // 1's cell no longer holds the leaver: a fresh offer starts clean
        // instead of cancelling against the stale target.
        Assert.IsType<LobbyChange.SwapOffered>(lobby.RequestSwap(Player(1), 4)!.Change);
    }

    [Fact]
    public void RepeatingReadinessIsANoOp()
    {
        LobbySession lobby = Lobby(teams: false, 1);

        Assert.NotNull(lobby.SetReady(Player(1), true));
        Assert.Null(lobby.SetReady(Player(1), true));
    }

    [Fact]
    public void UpdatesFreezeTheSnapshotAtTheMutationBoundary()
    {
        LobbySession lobby = new();
        LobbyUpdate joined = Assert.IsType<LobbyUpdate>(lobby.Join(Player(1)));

        lobby.SetReady(Player(1), true);

        Assert.False(joined.Snapshot.Members[0].Ready);
        Assert.False(joined.CanStart);
        Assert.True(lobby.Snapshot.Members[0].Ready);
    }

    private Player Player(int peerId)
    {
        if (_players.TryGetValue(peerId, out Player? player))
            return player;

        player = new Player(peerId, $"Player {peerId}", serverKeyCount: 0,
            serverGeneration: 1);
        _players.Add(peerId, player);
        return player;
    }
}
