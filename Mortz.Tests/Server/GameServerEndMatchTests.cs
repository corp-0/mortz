using System.Text;
using Mortz.Core.Admin;
using Mortz.Core.Net.Admin;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>The return to the lobby, driven the only way a client can drive it:
/// a real admin grant and a signed END_MATCH. Covers the admin handshake and
/// the RETURN_TO_LOBBY half of the transition machinery in one pass.</summary>
public class GameServerEndMatchTests : IDisposable
{
    private const string PASSWORD = "correct horse battery staple with entropy";
    private const int ADMIN = 7;

    private readonly TestServer _server = new(PASSWORD, allowJoinInProgress: false);

    public void Dispose() => _server.Dispose();

    [Fact]
    public void AdminAuthenticationIsAnsweredInTheLobby()
    {
        _server.Connect(ADMIN, "alice");

        byte[] sessionKey = Authenticate();

        Assert.NotEmpty(sessionKey);
        Assert.True(_server.Link.Last<AdminStateMsg>().IsAdmin);
    }

    [Fact]
    public void AdminAuthenticationIsRefusedDuringAMatchWithoutDroppingTheGrant()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Receive(ADMIN, new AdminAuthRequestMsg());

        AdminStateMsg state = _server.Link.Last<AdminStateMsg>();
        Assert.Contains("lobby", state.Status, StringComparison.OrdinalIgnoreCase);
        // The refusal reports the standing grant rather than revoking it, so a
        // mistimed /admin does not cost an authenticated admin their session.
        Assert.True(state.IsAdmin);
        EndMatch(sessionKey);
        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
    }

    [Fact]
    public void AnUnauthenticatedPlayerIsToldTheLobbyRuleDuringAMatch()
    {
        SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Receive(8, new AdminAuthRequestMsg());

        AdminStateMsg state = _server.Link.Last<AdminStateMsg>();
        Assert.False(state.IsAdmin);
        Assert.Contains("lobby", state.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnsignedEndMatchRequestChangesNothing()
    {
        SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Receive(ADMIN, new EndMatchRequestMsg(1, new byte[AdminCrypto.TAG_BYTES]));
        _server.Tick();

        Assert.Equal(ServerPhaseKind.MATCH, _server.Server.Phase);
        Assert.DoesNotContain("all:LobbyStateMsg", _server.Link.Trace());
    }

    [Fact]
    public void ASignedEndMatchReturnsToTheLobby()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        EndMatch(sessionKey);

        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
    }

    [Fact]
    public void ReturningToTheLobbyBroadcastsTheRosterFirst()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        EndMatch(sessionKey);

        string[] trace = _server.Link.Trace();
        Assert.Equal("7:LobbyLoadMsg", trace[0]);
    }

    [Fact]
    public void ReturningToTheLobbyRepublishesTheSettingsAndAnnouncesTheEnd()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        EndMatch(sessionKey);

        string[] trace = _server.Link.Trace();
        int roster = Array.IndexOf(trace, "7:LobbyStateMsg");
        int settings = Array.IndexOf(trace, "7:LobbySettingsMsg");
        Assert.True(settings > roster,
            $"settings ride the phase change, got {string.Join(", ", trace)}");
        Assert.Contains("ended the match", _server.Link.Last<ChatMsg>().Text);
    }

    [Fact]
    public void TheNewLobbySeatsEverybodyWhoIsStillConnected()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Link.Messages.Clear();

        EndMatch(sessionKey);

        LobbyStateMsg lobby = _server.Link.Last<LobbyStateMsg>();
        Assert.Equal([7, 8], lobby.Members.Select(member => member.PeerId));
        Assert.All(lobby.Members, member => Assert.False(member.Ready));
    }

    [Fact]
    public void AJipSpectatorJoinsTheNextLobby()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        _server.Connect(9, "spectator");

        EndMatch(sessionKey);

        LobbyStateMsg lobby = _server.Link.Last<LobbyStateMsg>();
        Assert.Equal([7, 8, 9], lobby.Members.Select(member => member.PeerId));
        Assert.All(lobby.Members, member => Assert.False(member.Ready));
    }

    [Fact]
    public void ANewMatchCanBeStartedAfterReturning()
    {
        byte[] sessionKey = SeatWithAnAdmin();
        _server.Tick();
        EndMatch(sessionKey);

        _server.Receive(7, new SetReadyMsg(true));
        _server.Receive(8, new SetReadyMsg(true));
        _server.Tick();

        Assert.Equal(ServerPhaseKind.MATCH, _server.Server.Phase);
    }

    private byte[] SeatWithAnAdmin()
    {
        _server.Connect(ADMIN, "alice");
        _server.Connect(8, "bob");
        byte[] sessionKey = Authenticate();
        _server.Receive(ADMIN, new SetReadyMsg(true));
        _server.Receive(8, new SetReadyMsg(true));
        return sessionKey;
    }

    /// <summary>The client half of the challenge handshake, real crypto and all.</summary>
    private byte[] Authenticate()
    {
        _server.Receive(ADMIN, new AdminAuthRequestMsg());
        byte[] challenge = _server.Link.Last<AdminChallengeMsg>().Challenge;
        byte[] passwordKey = AdminCrypto.DerivePasswordKey(
            Encoding.UTF8.GetBytes(PASSWORD), challenge);
        _server.Receive(ADMIN,
            new AdminProofMsg(AdminCrypto.ComputeProof(passwordKey, ADMIN, challenge)));
        return AdminCrypto.DeriveSessionKey(passwordKey, ADMIN, challenge);
    }

    private void EndMatch(byte[] sessionKey)
    {
        byte[] tag = AdminCrypto.ComputeCommandTag(sessionKey, ADMIN, 1,
            EndMatchAction.ACTION, EndMatchAction.SignablePayload());
        _server.Receive(ADMIN, new EndMatchRequestMsg(1, tag));
        _server.Tick();
    }
}
