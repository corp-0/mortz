using Mortz.Server;
using Xunit;

namespace Mortz.Tests.Server;

public class LobbySessionTests
{
    [Fact]
    public void LobbyStartsOnlyWhenEveryConnectedPlayerIsReady()
    {
        LobbySession lobby = LobbySession.For([3, 1]);

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
        LobbySession lobby = LobbySession.For([1, 2]);
        lobby.SetReady(1, true);

        lobby.Remove(2);

        Assert.True(lobby.CanStart);
    }

    [Fact]
    public void EnablingTeamsDealsEveryoneOutBalanced()
    {
        LobbySession lobby = LobbySession.For([1, 2, 3]);

        Assert.True(lobby.SetTeamsEnabled(true));

        Assert.Equal([(byte)1, (byte)2, (byte)1], lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void TeamToggleWithoutTransitionChangesNothing()
    {
        LobbySession lobby = LobbySession.For([1, 2]);

        Assert.False(lobby.SetTeamsEnabled(false));
        Assert.True(lobby.SetTeamsEnabled(true));
        Assert.False(lobby.SetTeamsEnabled(true));
    }

    [Fact]
    public void JoinersLandOnTheSmallestTeamAndLeaversReshuffleNobody()
    {
        LobbySession lobby = LobbySession.For([1, 2, 3], teamsEnabled: true);

        lobby.Remove(1); // team 1 loses a member, leaving 3 alone on it
        lobby.Add(4);    // ties break to team 1, so it fills back up
        lobby.Add(5);    // team 1 now outnumbers, so 5 lands on team 2

        Assert.Equal([(byte)2, (byte)1, (byte)1, (byte)2],
            lobby.Players.Select(player => player.Team));
        Assert.Equal([2L, 3L, 4L, 5L], lobby.Players.Select(player => player.PeerId));
    }

    [Fact]
    public void DisablingTeamsClearsAssignmentsAndReenablingDealsFresh()
    {
        LobbySession lobby = LobbySession.For([1, 2], teamsEnabled: true);

        Assert.True(lobby.SetTeamsEnabled(false));
        Assert.All(lobby.Players, player => Assert.Equal(0, player.Team));

        Assert.True(lobby.SetTeamsEnabled(true));
        Assert.Equal([(byte)1, (byte)2], lobby.Players.Select(player => player.Team));
    }

    [Fact]
    public void ReadyStatePersistsThroughTeamToggles()
    {
        LobbySession lobby = LobbySession.For([1, 2]);
        lobby.SetReady(1, true);

        lobby.SetTeamsEnabled(true);
        lobby.SetTeamsEnabled(false);

        Assert.Equal([true, false], lobby.Players.Select(player => player.Ready));
    }

    [Fact]
    public void EmptyLobbyTeamToggleNeedsNoBroadcast()
    {
        LobbySession lobby = new();

        Assert.False(lobby.SetTeamsEnabled(true));

        lobby.Add(1);
        Assert.Equal((byte)1, lobby.Players[0].Team);
    }
}
