using System.Text;
using Mortz.Core.Admin;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Admin;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Roster;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Tests.Server;

public class GameServerLobbyMutationTests
{
    private const string ADMIN_PASSWORD = "correct horse battery staple with entropy";

    [Fact]
    public void SuccessfulReadinessMutationBroadcastsOnceAndStartsOnlyWhenResultCanStart()
    {
        using TestServer server = new();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Link.Messages.Clear();

        server.Receive(7, new SetReadyMsg(true));
        Assert.Single(Broadcasts(server));
        server.Tick();
        Assert.Equal(ServerPhaseKind.LOBBY, server.Server.Phase);

        server.Link.Messages.Clear();
        server.Receive(8, new SetReadyMsg(true));
        Assert.Single(Broadcasts(server));
        server.Tick();
        Assert.Equal(ServerPhaseKind.MATCH, server.Server.Phase);
    }

    [Fact]
    public void RepeatedReadinessDoesNotBroadcastAgain()
    {
        using TestServer server = new();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Receive(7, new SetReadyMsg(true));
        server.Link.Messages.Clear();

        server.Receive(7, new SetReadyMsg(true));

        Assert.Empty(Broadcasts(server));
    }

    [Fact]
    public void SuccessfulTeamMoveBroadcastsOnce()
    {
        using TestServer server = TeamServer();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Connect(9, "carol");
        server.Link.Messages.Clear();

        server.Receive(7, new TeamJoinRequestMsg(TeamWire.ToByte(Team.RED)));

        Assert.Single(Broadcasts(server));
        Assert.Equal(Team.RED,
            server.Link.Last<LobbyStateMsg>().Members.Single(member => member.PeerId == 7).Team);

        server.Link.Messages.Clear();
        server.Receive(7, new TeamJoinRequestMsg(TeamWire.ToByte(Team.RED)));
        Assert.Empty(Broadcasts(server));
    }

    [Fact]
    public void OfferCancellationAndReciprocalSwapEachBroadcastOnce()
    {
        using TestServer server = TeamServer();
        server.Connect(7, "alice");
        server.Connect(8, "bob");

        server.Link.Messages.Clear();
        server.Receive(7, new TeamSwapRequestMsg(8));
        Assert.Single(Broadcasts(server));

        server.Link.Messages.Clear();
        server.Receive(7, new TeamSwapRequestMsg(8));
        Assert.Single(Broadcasts(server));

        server.Link.Messages.Clear();
        server.Receive(7, new TeamSwapRequestMsg(8));
        Assert.Single(Broadcasts(server));

        server.Link.Messages.Clear();
        server.Receive(8, new TeamSwapRequestMsg(7));
        Assert.Single(Broadcasts(server));
        Assert.Empty(server.Link.Last<LobbyStateMsg>().Offers);
    }

    [Fact]
    public void FailedTeamAndSwapMutationsDoNotBroadcast()
    {
        using TestServer server = new();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Link.Messages.Clear();

        server.Receive(7, new TeamJoinRequestMsg(TeamWire.ToByte(Team.RED)));
        server.Receive(7, new TeamSwapRequestMsg(99));

        Assert.Empty(Broadcasts(server));
    }

    [Fact]
    public void SettingsTeamRulePublishesLobbyStateOnce()
    {
        using TestServer server = new(adminPassword: ADMIN_PASSWORD);
        server.Connect(7, "alice");
        byte[] sessionKey = Authenticate(server, 7);
        byte[] config = new MatchConfig
        {
            Rules = new ModeRules { Teams = true },
        }.ToBytes();
        byte[] tag = AdminCrypto.ComputeCommandTag(
            sessionKey, 7, 1, ReplaceLobbyRulesAction.ACTION,
            ReplaceLobbyRulesAction.SignablePayload(config));
        server.Link.Messages.Clear();

        server.Receive(7, new LobbyRulesUpdateMsg(config, 1, tag));

        Assert.Single(Broadcasts(server));
        Assert.Equal(Team.BLUE, server.Link.Last<LobbyStateMsg>().Members[0].Team);
        Assert.Equal("[color=#4dff21]alice[/color] changed [b]Teams[/b] Off > On",
            server.Link.Last<ChatMsg>().Text);
    }

    [Fact]
    public void RejectedSettingsMutationResyncsOnlyTheSender()
    {
        using TestServer server = new(adminPassword: ADMIN_PASSWORD);
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        byte[] sessionKey = Authenticate(server, 7);
        byte[] tag = AdminCrypto.ComputeCommandTag(
            sessionKey, 7, 1, SetLobbyMapAction.ACTION,
            SetLobbyMapAction.SignablePayload("missing"));
        server.Link.Messages.Clear();

        server.Receive(7, new LobbyMapUpdateMsg("missing", 1, tag));

        Sent resync = Assert.Single(server.Link.Messages,
            sent => sent.Message is LobbySettingsMsg);
        Assert.Equal(7, resync.Target);
        Assert.Empty(Broadcasts(server));
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Message is ChatMsg);
    }

    [Fact]
    public void AppliedNoOpBroadcastsCurrentSettingsWithoutPublishingTheLobby()
    {
        using TestServer server = new(adminPassword: ADMIN_PASSWORD);
        server.Connect(7, "alice");
        byte[] sessionKey = Authenticate(server, 7);
        byte[] config = server.Boot.Rules.ToBytes();
        byte[] tag = AdminCrypto.ComputeCommandTag(
            sessionKey, 7, 1, ReplaceLobbyRulesAction.ACTION,
            ReplaceLobbyRulesAction.SignablePayload(config));
        server.Link.Messages.Clear();

        server.Receive(7, new LobbyRulesUpdateMsg(config, 1, tag));

        Sent settings = Assert.Single(server.Link.Messages,
            sent => sent.Message is LobbySettingsMsg);
        Assert.Equal(0, settings.Target);
        Assert.Empty(Broadcasts(server));
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Message is ChatMsg);
    }

    [Fact]
    public void AppliedSettingsSurviveMatchAndLobbyLifetimes()
    {
        using TestServer server = new(adminPassword: ADMIN_PASSWORD);
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        byte[] sessionKey = Authenticate(server, 7);
        byte[] config = new MatchConfig
        {
            Rules = new ModeRules { Teams = true },
        }.ToBytes();
        byte[] updateTag = AdminCrypto.ComputeCommandTag(
            sessionKey, 7, 1, ReplaceLobbyRulesAction.ACTION,
            ReplaceLobbyRulesAction.SignablePayload(config));
        server.Receive(7, new LobbyRulesUpdateMsg(config, 1, updateTag));
        server.Receive(7, new SetReadyMsg(true));
        server.Receive(8, new SetReadyMsg(true));
        server.Tick();

        byte[] endTag = AdminCrypto.ComputeCommandTag(
            sessionKey, 7, 2, EndMatchAction.ACTION, EndMatchAction.SignablePayload());
        server.Receive(7, new EndMatchRequestMsg(2, endTag));
        server.Tick();

        MatchConfig persisted = MatchConfig.FromBytes(server.Link.Last<LobbySettingsMsg>().Config);
        Assert.Equal(ServerPhaseKind.LOBBY, server.Server.Phase);
        Assert.True(persisted.Rules.Teams);
    }

    private static TestServer TeamServer() => new(rules: new MatchConfig
    {
        Rules = new ModeRules { Teams = true },
    });

    private static IEnumerable<Sent> Broadcasts(TestServer server) =>
        server.Link.Messages.Where(sent => sent is { Target: 0, Message: LobbyStateMsg });

    private static byte[] Authenticate(TestServer server, int peerId)
    {
        server.Receive(peerId, new AdminAuthRequestMsg());
        byte[] challenge = server.Link.Last<AdminChallengeMsg>().Challenge;
        byte[] passwordKey = AdminCrypto.DerivePasswordKey(
            Encoding.UTF8.GetBytes(ADMIN_PASSWORD), challenge);
        server.Receive(peerId,
            new AdminProofMsg(AdminCrypto.ComputeProof(passwordKey, peerId, challenge)));
        return AdminCrypto.DeriveSessionKey(passwordKey, peerId, challenge);
    }
}
