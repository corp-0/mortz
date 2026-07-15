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
}
