using Mortz.Core.Identity;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Chat;
using Mortz.Protocol.Net.Lobby;
using Mortz.Protocol.Net.Query;
using Mortz.Protocol.Net.Stats;
using Mortz.Server.Diagnostics;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

/// <summary>The connect and disconnect flow, read off the wire: who is told
/// what, and in which order.</summary>
public class GameServerConnectTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmittedAccountSurvivesMatchEntryAndDisconnect(bool verified)
    {
        AccountObserver observer = new();
        using TestServer server = new(observer: observer);
        VerifiedAccount? account = verified ? new(AccountProvider.STEAM, ulong.MaxValue) : null;

        server.Server.Connect(new(7, "alice", 4, account));
        server.Ready(7);
        Player player = Assert.IsType<Player>(observer.Joined);
        Assert.Equal(account, player.Account);
        Assert.Equal(4, player.Skin);

        server.Receive(7, new SetReadyMsg(true));
        server.Tick();
        Assert.Equal(ServerPhaseKind.MATCH, server.Server.Phase);
        Assert.Equal(account, player.Account);

        server.Server.Disconnect(7);
        Assert.Same(player, observer.Left);
        Assert.Equal(account, Assert.IsType<Player>(observer.Left).Account);
        Assert.Equal(0, server.Server.PlayerCount);
    }

    private class AccountObserver : IMatchObserver
    {
        public Player? Joined;
        public Player? Left;
        public void PlayerJoined(Player player, ServerPhaseKind phase) => Joined = player;
        public void PlayerLeft(Player player, ServerPhaseKind phase) => Left = player;
        public void PhaseChanged(ServerPhaseKind kind) { }
        public void MatchAdvanced(MatchUpdate update) { }
    }

    [Fact]
    public void JoiningAnnouncesThenSeatsThenCatchesTheArrivalUp()
    {
        _server.Connect(7, "alice");

        Assert.Equal(
        [
            "7:LobbyLoadMsg",
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
        Assert.Equal([7, 8], wins.Rows.Select(row => row.PeerId));
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
        int settings = Array.IndexOf(trace, "8:LobbySettingsMsg");
        Assert.True(settings >= 0 && roster > settings,
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

    [Theory]
    [InlineData("2.4.0")]
    [InlineData("2.4.0-preview.1+abc123")]
    public void QueryReportsTheSuppliedBuildVersion(string version)
    {
        using TestServer server = new(applicationVersion: version);
        ServerInfo metadata = server.Server.Describe();
        Assert.Equal(NetConfig.PROTOCOL_VERSION, metadata.ProtocolVersion);
        Assert.True(ServerQueryProtocol.TryDecodeInfo(
            ServerQueryProtocol.EncodeInfoResponse(metadata), 7777, out ServerInfo decoded));
        Assert.Equal(version, decoded.Version);
    }
}
