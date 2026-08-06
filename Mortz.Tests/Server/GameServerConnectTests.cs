using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Stats;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>The connect and disconnect flow, read off the wire: who is told
/// what, and in which order.</summary>
public class GameServerConnectTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public void JoiningAnnouncesThenSeatsThenCatchesTheArrivalUp()
    {
        _server.Connect(7, "alice");

        Assert.Equal(
        [
            "7:PhaseLoadMsg",
            "7:LobbySettingsMsg",
            "7:ChatMsg",
            "7:SessionWinsMsg",
            "7:LobbyStateMsg",
        ], _server.Link.Trace());
    }

    [Fact]
    public void TheCatchUpSeesTheArrivalItself()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Connect(8, "bob");

        // Bob's own wins row is in the table he receives, so Sync ran after the join.
        SessionWinsMsg wins = _server.Link.Last<SessionWinsMsg>();
        Assert.Equal([7, 8], wins.PeerIds);
        LobbyStateMsg roster = _server.Link.Last<LobbyStateMsg>();
        Assert.Equal(2, roster.Members.Length);
    }

    [Fact]
    public void TheRosterBroadcastPrecedesEveryUnicastCatchUp()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Connect(8, "bob");

        string[] trace = _server.Link.Trace();
        int roster = Array.IndexOf(trace, "8:LobbyStateMsg");
        int welcome = Array.IndexOf(trace, "8:LobbySettingsMsg");
        Assert.True(welcome >= 0 && roster > welcome,
            $"join observers must flush before the phase roster, got {string.Join(", ", trace)}");
    }

    [Fact]
    public void LeavingFansOutInTheReverseOrderOfJoining()
    {
        _server.Connect(7, "alice");
        _server.Receive(7, new TypingMsg(true));
        _server.Link.Messages.Clear();

        _server.Server.Disconnect(7);

        // Lobby seat first, then the server observers backwards: typing before chat.
        Assert.Equal(
        [
            "all:LobbyStateMsg",
            "all:TypingStateMsg",
            "all:ChatMsg",
        ], _server.Link.Trace());
    }

    [Fact]
    public void TheLeaverIsOutOfTheRosterButStillNamed()
    {
        _server.Connect(7, "alice");
        _server.Connect(8, "bob");
        _server.Link.Messages.Clear();

        _server.Server.Disconnect(7);

        Assert.Equal(["bob"],
            _server.Link.Last<LobbyStateMsg>().Members.Select(member => member.Name));
        Assert.Contains("alice", _server.Link.Last<ChatMsg>().Text);
    }

    [Fact]
    public void DisconnectingAStrangerDoesNothing()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Server.Disconnect(99);

        Assert.Empty(_server.Link.Messages);
        Assert.Equal(1, _server.Server.PlayerCount);
    }

    [Fact]
    public void PlayerCountFollowsTheRoster()
    {
        Assert.Equal(0, _server.Server.PlayerCount);

        _server.Connect(7, "alice");
        _server.Connect(8, "bob");
        Assert.Equal(2, _server.Server.PlayerCount);

        _server.Server.Disconnect(7);
        Assert.Equal(1, _server.Server.PlayerCount);
    }

    [Fact]
    public void TheServerBootsIntoTheLobbyWithNobodyToldAnything()
    {
        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void DescribeReportsTheLiveRosterAndPhase()
    {
        _server.Connect(7, "alice");

        Assert.Equal(1, _server.Server.Describe().Players);
        Assert.True(_server.Server.Describe().InLobby);
        Assert.Equal("test", _server.Server.Describe().Name);
        Assert.Equal("Arena", _server.Server.Describe().Map);
    }
}
