using Mortz.Client.Session;
using Xunit;

namespace Mortz.Tests.Client;

public class ClientSessionTests
{
    [Fact]
    public void SessionTracksTheClientFlow()
    {
        ClientSession session = new();
        Assert.Equal(ClientSessionStage.MENU, session.Stage);

        Assert.True(session.TryBeginConnecting());
        Assert.True(session.TryEnterLobby());
        Assert.True(session.TryBeginMatchLoad());
        Assert.True(session.TryEnterMatch());

        Assert.Equal(ClientSessionStage.PLAYING, session.Stage);

        session.ReturnToMenu();
        Assert.Equal(ClientSessionStage.MENU, session.Stage);
    }

    [Fact]
    public void SessionRejectsMatchMessagesWhileAtTheMenu()
    {
        ClientSession session = new();

        Assert.False(session.TryBeginMatchLoad());
        Assert.False(session.TryEnterMatch());
        Assert.False(session.TryEnterLobby());
        Assert.Equal(ClientSessionStage.MENU, session.Stage);
    }

    [Fact]
    public void SessionRejectsASecondConnectionOnceAdmitted()
    {
        ClientSession session = new();
        Assert.True(session.TryBeginConnecting());

        // Still at the menu waiting on the server, so a different address is fine.
        Assert.True(session.TryBeginConnecting());

        Assert.True(session.TryEnterLobby());
        Assert.False(session.TryBeginConnecting());

        Assert.True(session.TryBeginMatchLoad());
        Assert.False(session.TryBeginConnecting());

        Assert.True(session.TryEnterMatch());
        Assert.False(session.TryBeginConnecting());
        Assert.Equal(ClientSessionStage.PLAYING, session.Stage);

        session.ReturnToMenu();
        Assert.True(session.TryBeginConnecting());
    }
}
