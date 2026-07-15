using Mortz.Client;
using Xunit;

namespace Mortz.Tests.Client;

public class ClientSessionTests
{
    [Fact]
    public void SessionTracksTheClientFlow()
    {
        ClientSession session = new();
        Assert.Equal(ClientSessionStage.Menu, session.Stage);

        session.BeginConnecting();
        Assert.True(session.TryEnterLobby());
        Assert.True(session.TryBeginMatchLoad());
        Assert.True(session.TryEnterMatch());

        Assert.Equal(ClientSessionStage.Playing, session.Stage);

        session.ReturnToMenu();
        Assert.Equal(ClientSessionStage.Menu, session.Stage);
    }

    [Fact]
    public void SessionRejectsMatchMessagesWhileAtTheMenu()
    {
        ClientSession session = new();

        Assert.False(session.TryBeginMatchLoad());
        Assert.False(session.TryEnterMatch());
        Assert.False(session.TryEnterLobby());
        Assert.Equal(ClientSessionStage.Menu, session.Stage);
    }
}
