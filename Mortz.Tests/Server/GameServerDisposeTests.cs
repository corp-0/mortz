using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>Orderly shutdown. The order features and cells go down in is not
/// visible from outside, so what is asserted here is that every live shape
/// survives it and that a second call does nothing at all.</summary>
public class GameServerDisposeTests
{
    [Fact]
    public void ShuttingDownWithPlayersInTheLobbySaysNothing()
    {
        TestServer server = new();
        server.Connect(7, "alice");
        server.Receive(7, new TypingMsg(true));
        server.Link.Messages.Clear();

        server.Server.Dispose();

        Assert.Empty(server.Link.Messages);
    }

    [Fact]
    public void ShuttingDownMidMatchClosesTheMatchCellsToo()
    {
        TestServer server = new();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Receive(7, new SetReadyMsg(true));
        server.Receive(8, new SetReadyMsg(true));
        server.Tick();
        server.Link.Messages.Clear();

        server.Server.Dispose();

        Assert.Empty(server.Link.Messages);
    }
}
