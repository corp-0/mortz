using Mortz.Core.Chat;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Admin;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Roster;
using Mortz.Core.Net.Score;
using Mortz.Core.Net.Sim;
using Mortz.Core.Net.Stats;
using Mortz.Core.Replication;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Tests.Net;
using Xunit;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Tests.Core.Net;

/// <summary>The id and bytes one send put on the wire.</summary>
public readonly record struct SentEnvelope(ushort Id, byte[] Payload);

/// <summary>Full protocol round-trips over a loopback NetTransport, no socket:
/// down the wire it's serializer -> NetRouter -> IHandle, up the wire it's
/// SendToServer -> NetRouter&lt;TSender&gt; -> IHandle. All tests share the
/// NetTransport.Send static, so every class that swaps it joins the
/// NetTransport collection (xunit runs it sequentially) and restores it
/// after.</summary>
[Collection("NetTransport")]
public class NetMessageTests : IDisposable
{
    private const int SENDER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    /// <summary>Loopback into a client router, where server-to-client lands.
    /// Register the probes before sending.</summary>
    private static NetRouter UseClientLoopback()
    {
        NetRouter router = new();
        NetTransport.Send = (id, payload, _, _) => Assert.True(router.Dispatch(id, payload));
        return router;
    }

    /// <summary>Loopback into a server router, where client-to-server lands.
    /// Register the probes before sending.</summary>
    private static NetRouter<int> UseServerLoopback()
    {
        NetRouter<int> router = new();
        NetTransport.Send = (id, payload, _, _) => Assert.True(router.Dispatch(id, SENDER, payload));
        return router;
    }

    [Fact]
    public void RosterMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<RosterMsg> probe = new();
        router.Add(probe);

        RosterEntry[] entries =
        [
            new RosterEntry(1, "Gilles", 3, Team.BLUE, 1),
            new RosterEntry(1789001122, "Player 2", 7, Team.RED, 2),
        ];
        new RosterMsg(entries).Broadcast();

        Assert.Equal(entries, Assert.Single(probe.Messages).Entries);
    }

    [Fact]
    public void PingUpdateMsg_RoundTripsARowArray()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<PingUpdateMsg> probe = new();
        router.Add(probe);

        new PingUpdateMsg([new PeerPing(11, 30), new PeerPing(1789001122, 120)]).Broadcast();

        Assert.Equal([new PeerPing(11, 30), new PeerPing(1789001122, 120)],
            Assert.Single(probe.Messages).Pings);
    }

    [Fact]
    public void PingUpdateMsg_RoundTripsAnEmptyTable()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<PingUpdateMsg> probe = new();
        router.Add(probe);

        new PingUpdateMsg([]).Broadcast();

        Assert.Empty(Assert.Single(probe.Messages).Pings);
    }

    [Fact]
    public void Dispatch_RejectsNegativeHugeAndTruncatedRowCounts()
    {
        NetRouter router = new();
        ClientProbe<PingUpdateMsg> probe = new();
        router.Add(probe);

        Assert.False(router.Dispatch(NetRegistry.ID_PingUpdateMsg,
            Bytes(w => w.Write(-1))));
        Assert.False(router.Dispatch(NetRegistry.ID_PingUpdateMsg,
            Bytes(w => w.Write(NetConfig.MAX_ARRAY_ELEMENTS + 1))));
        // Two rows promised, one delivered.
        Assert.False(router.Dispatch(NetRegistry.ID_PingUpdateMsg,
            Bytes(w => { w.Write(2); w.Write(11); w.Write(30); })));
        // One row promised, two delivered: trailing bytes.
        Assert.False(router.Dispatch(NetRegistry.ID_PingUpdateMsg,
            Bytes(w => { w.Write(1); w.Write(11); w.Write(30); w.Write(22); w.Write(40); })));
        Assert.Empty(probe.Messages);
    }

    [Fact]
    public void PlayerModifiersMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<PlayerModifiersMsg> probe = new();
        router.Add(probe);
        byte[] blob = ModifierWire.Serialize([Modifiers.Water]);

        new PlayerModifiersMsg(1789001122, blob).Broadcast();

        PlayerModifiersMsg received = Assert.Single(probe.Messages);
        Assert.Equal(1789001122, received.PeerId);
        StatsModifier got = Assert.Single(ModifierWire.Deserialize(received.Modifiers));
        Assert.Equal(ModifierId.WATER, got.Id);
        Assert.Equal(Modifiers.Water.Changes, got.Changes);
    }

    [Fact]
    public void LobbyStateMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<LobbyStateMsg> probe = new();
        router.Add(probe);

        LobbyMember[] members =
        [
            new LobbyMember(5, "a", true, Team.BLUE),
            new LobbyMember(6, "", false, Team.RED),
        ];
        new LobbyStateMsg(members, [new SwapOffer(5, 6)]).Broadcast();

        LobbyStateMsg received = Assert.Single(probe.Messages);
        Assert.Equal(members, received.Members);
        Assert.Equal([new SwapOffer(5, 6)], received.Offers);
    }

    [Fact]
    public void ScoreSyncMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<ScoreSyncMsg> probe = new();
        router.Add(probe);

        new ScoreSyncMsg([new ScoreRow(7, 4, 2), new ScoreRow(8, -1, 6)], 4, -1).SendTo(9);

        ScoreSyncMsg received = Assert.Single(probe.Messages);
        Assert.Equal([new ScoreRow(7, 4, 2), new ScoreRow(8, -1, 6)], received.Rows);
        Assert.Equal(4, received.BlueKills);
        Assert.Equal(-1, received.RedKills);
    }

    [Fact]
    public void LobbySettingsMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<LobbySettingsMsg> probe = new();
        router.Add(probe);
        byte[] config = new MatchConfig
        {
            Physics = new Physics { Gravity = 321 },
        }.ToBytes();

        new LobbySettingsMsg("castlewars", "hash", ["arena", "castlewars"],
            ["Arena", "Castle Wars"], ["deathmatch"], ["Deathmatch"], "deathmatch",
            config).Broadcast();

        LobbySettingsMsg received = Assert.Single(probe.Messages);
        Assert.Equal("castlewars", received.MapId);
        Assert.Equal("hash", received.MapHash);
        Assert.Equal(["arena", "castlewars"], received.MapIds);
        Assert.Equal(["Arena", "Castle Wars"], received.MapNames);
        Assert.Equal(config, received.Config);
    }

    [Fact]
    public void MatchLoadMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<MatchLoadMsg> probe = new();
        router.Add(probe);
        byte[] config = TestWorlds.NoSpawnProtectionConfig.ToBytes();

        new MatchLoadMsg("castlewars", "abc123", config,
            (byte)TerrainSyncEncoding.CARVE_LOG, 17, 12345, 2,
            MatchSeat.SPECTATOR, MatchActivity.SPECTATING, SpectateReason.JIP, -1,
            new Snapshot(12, [], []).SerializeFor(5), -1).SendTo(5);

        MatchLoadMsg received = Assert.Single(probe.Messages);
        Assert.Equal("castlewars", received.MapId);
        Assert.Equal("abc123", received.MapHash);
        Assert.Equal(config, received.Config);
        Assert.Equal((byte)TerrainSyncEncoding.CARVE_LOG, received.TerrainEncoding);
        Assert.Equal(17, received.TerrainTransferId);
        Assert.Equal(12345, received.TerrainBytes);
        Assert.Equal(2, received.TerrainChunks);
        Assert.Equal(MatchSeat.SPECTATOR, received.Seat);
        Assert.Equal(MatchActivity.SPECTATING, received.Activity);
        Assert.Equal(SpectateReason.JIP, received.SpectateReason);
    }

    [Fact]
    public void CarveMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<CarveMsg> probe = new();
        router.Add(probe);

        new CarveMsg(1986, 972, 12, 1646958266, -1).Broadcast();

        Assert.Equal(new CarveMsg(1986, 972, 12, 1646958266, -1), Assert.Single(probe.Messages));
    }

    [Fact]
    public void ShellRetireMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<ShellRetireMsg> probe = new();
        router.Add(probe);

        new ShellRetireMsg(314).SendTo(7);

        Assert.Equal(new ShellRetireMsg(314), Assert.Single(probe.Messages));
    }

    [Fact]
    public void DeathMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<DeathMsg> probe = new();
        router.Add(probe);

        new DeathMsg(1234567890, -5, 7).Broadcast();

        Assert.Equal(new DeathMsg(1234567890, -5, 7), Assert.Single(probe.Messages));
    }

    [Fact]
    public void EliminationMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<EliminationMsg> probe = new();
        router.Add(probe);

        new EliminationMsg(1234567890, 42,
            EliminationFlags.FIRST_BLOOD | EliminationFlags.OWNED,
            -2, 7, 9, 4, 5, 3).Broadcast();

        Assert.Equal(new EliminationMsg(1234567890, 42,
            EliminationFlags.FIRST_BLOOD | EliminationFlags.OWNED,
            -2, 7, 9, 4, 5, 3), Assert.Single(probe.Messages));
    }

    [Fact]
    public void MatchEndMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<MatchEndMsg> probe = new();
        router.Add(probe);

        new MatchEndMsg(true, 2).Broadcast();

        Assert.Equal(new MatchEndMsg(true, 2), Assert.Single(probe.Messages));
    }

    [Fact]
    public void ChatAndAdminServerMessages_RoundTrip()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<ChatMsg> chat = new();
        ClientProbe<AdminChallengeMsg> challenge = new();
        ClientProbe<AdminStateMsg> state = new();
        router.Add(chat);
        router.Add(challenge);
        router.Add(state);

        ChatProtocol.Encode(new ChatLine.Player(42, "Alice", "hello 🐛")).Broadcast();
        new AdminChallengeMsg([1, 2, 3]).SendTo(42);
        new AdminStateMsg(true, "granted").SendTo(42);

        Assert.True(ChatProtocol.TryDecode(Assert.Single(chat.Messages),
            out ChatLine.Remote? line));
        Assert.Equal(new ChatLine.Player(42, "Alice", "hello 🐛"), line);
        Assert.Equal([1, 2, 3], Assert.Single(challenge.Messages).Challenge);
        Assert.Equal(new AdminStateMsg(true, "granted"), Assert.Single(state.Messages));
    }

    [Fact]
    public void FinalKillMsg_RoundTrips()
    {
        NetRouter router = UseClientLoopback();
        ClientProbe<FinalKillMsg> probe = new();
        router.Add(probe);
        FinalKillMsg expected = new(
            781, 12, 42,
            FinalKillFlags.EXPLOSION | FinalKillFlags.OWNED,
            -5, 700, 20, 680, 48);

        expected.Broadcast();

        Assert.Equal(expected, Assert.Single(probe.Messages));
    }

    [Fact]
    public void ClientToServerMsgs_RoundTrip_WithSender()
    {
        NetRouter<int> router = UseServerLoopback();
        Probe<SetReadyMsg> ready = new();
        Probe<TeamJoinRequestMsg> join = new();
        router.Add(ready);
        router.Add(join);

        new SetReadyMsg(true).SendToServer();
        new TeamJoinRequestMsg(2).SendToServer();

        Delivery<SetReadyMsg> readyDelivery = Assert.Single(ready.Deliveries);
        Assert.Equal(SENDER, readyDelivery.Sender);
        Assert.True(readyDelivery.Message.Ready);
        Delivery<TeamJoinRequestMsg> joinDelivery = Assert.Single(join.Deliveries);
        Assert.Equal(SENDER, joinDelivery.Sender);
        Assert.Equal(2, joinDelivery.Message.Team);
    }

    [Fact]
    public void ChatAndAdminClientMessages_RoundTrip_WithTransportSender()
    {
        NetRouter<int> router = UseServerLoopback();
        Probe<ChatSendMsg> chat = new();
        Probe<AdminAuthRequestMsg> request = new();
        Probe<AdminProofMsg> proof = new();
        router.Add(chat);
        router.Add(request);
        router.Add(proof);

        new ChatSendMsg("hello").SendToServer();
        new AdminAuthRequestMsg().SendToServer();
        new AdminProofMsg([7, 8, 9]).SendToServer();

        Delivery<ChatSendMsg> chatDelivery = Assert.Single(chat.Deliveries);
        Assert.Equal(SENDER, chatDelivery.Sender);
        Assert.Equal("hello", chatDelivery.Message.Text);
        Assert.Equal(SENDER, Assert.Single(request.Deliveries).Sender);
        Delivery<AdminProofMsg> proofDelivery = Assert.Single(proof.Deliveries);
        Assert.Equal(SENDER, proofDelivery.Sender);
        Assert.Equal([7, 8, 9], proofDelivery.Message.Proof);
    }

    [Fact]
    public void SignedLobbyUpdates_RoundTrip_WithUnsignedSequenceBitsIntact()
    {
        NetRouter<int> router = UseServerLoopback();
        Probe<LobbyRulesUpdateMsg> rules = new();
        Probe<LobbyMapUpdateMsg> map = new();
        router.Add(rules);
        router.Add(map);

        new LobbyRulesUpdateMsg([1, 2], ulong.MaxValue, [3, 4]).SendToServer();
        new LobbyMapUpdateMsg("arena", ulong.MaxValue - 1, [5, 6]).SendToServer();

        Delivery<LobbyRulesUpdateMsg> rulesDelivery = Assert.Single(rules.Deliveries);
        Assert.Equal(SENDER, rulesDelivery.Sender);
        Assert.Equal([1, 2], rulesDelivery.Message.Config);
        Assert.Equal(ulong.MaxValue, rulesDelivery.Message.Sequence);
        Assert.Equal([3, 4], rulesDelivery.Message.Tag);
        Delivery<LobbyMapUpdateMsg> mapDelivery = Assert.Single(map.Deliveries);
        Assert.Equal(SENDER, mapDelivery.Sender);
        Assert.Equal("arena", mapDelivery.Message.MapId);
        Assert.Equal(ulong.MaxValue - 1, mapDelivery.Message.Sequence);
        Assert.Equal([5, 6], mapDelivery.Message.Tag);
    }

    [Fact]
    public void RollRequestMsg_RoundTrips()
    {
        NetRouter<int> router = UseServerLoopback();
        Probe<RollRequestMsg> roll = new();
        router.Add(roll);

        new RollRequestMsg().SendToServer();

        Assert.Equal(SENDER, Assert.Single(roll.Deliveries).Sender);
    }

    [Fact]
    public void Dispatch_DropsWrongDirection()
    {
        // A client-only message arriving at the server (spoof) and vice versa.
        byte[] captured = [];
        NetTransport.Send = (_, payload, _, _) => captured = payload;
        NetRouter<int> serverRouter = new();
        Probe<SetReadyMsg> ready = new();
        serverRouter.Add(ready);
        NetRouter clientRouter = new();
        ClientProbe<RosterMsg> roster = new();
        clientRouter.Add(roster);

        new RosterMsg([new RosterEntry(1, "x", 2, null, 1)]).Broadcast();
        Assert.False(serverRouter.Dispatch(NetRegistry.ID_RosterMsg, SENDER, captured));
        new SetReadyMsg(true).SendToServer();
        Assert.False(clientRouter.Dispatch(NetRegistry.ID_SetReadyMsg, captured));

        Assert.Empty(roster.Messages);
        Assert.Empty(ready.Deliveries);
    }

    [Fact]
    public void Dispatch_DropsUnknownId()
    {
        Assert.False(new NetRouter().Dispatch(ushort.MaxValue, []));
    }

    [Fact]
    public void Dispatch_RejectsEveryClientMessageTruncationAndTrailingBytes()
    {
        SentEnvelope[] messages = [
            Capture(NetRegistry.ID_SetReadyMsg, () => new SetReadyMsg(true).SendToServer()),
            Capture(NetRegistry.ID_ChatSendMsg, () => new ChatSendMsg("hello").SendToServer()),
            Capture(NetRegistry.ID_AdminAuthRequestMsg, () => new AdminAuthRequestMsg().SendToServer()),
            Capture(NetRegistry.ID_AdminProofMsg, () => new AdminProofMsg([1, 2, 3]).SendToServer()),
            Capture(NetRegistry.ID_LobbyRulesUpdateMsg,
                () => new LobbyRulesUpdateMsg([1, 2], 3, [4, 5]).SendToServer()),
            Capture(NetRegistry.ID_LobbyMapUpdateMsg,
                () => new LobbyMapUpdateMsg("arena", 3, [4, 5]).SendToServer()),
            Capture(NetRegistry.ID_TeamJoinRequestMsg,
                () => new TeamJoinRequestMsg(1).SendToServer()),
            Capture(NetRegistry.ID_TeamSwapRequestMsg,
                () => new TeamSwapRequestMsg(42).SendToServer()),
        ];

        RaiseCounter raised = new();
        NetRouter<int> router = new();
        router.Add(raised);

        foreach (SentEnvelope message in messages)
        {
            for (int length = 0; length < message.Payload.Length; length++)
            {
                Assert.False(router.Dispatch(message.Id, SENDER, message.Payload[..length]));
            }
            Assert.False(router.Dispatch(message.Id, SENDER, [.. message.Payload, 0xA5]));
        }
        Assert.Equal(0, raised.Count);
    }

    [Fact]
    public void Dispatch_RejectsNegativeHugeAndTruncatedArrayLengths()
    {
        NetRouter router = new();
        ClientProbe<RosterMsg> roster = new();
        ClientProbe<MatchLoadMsg> matchLoad = new();
        router.Add(roster);
        router.Add(matchLoad);

        Assert.False(router.Dispatch(NetRegistry.ID_RosterMsg,
            Bytes(w => w.Write(-1))));
        Assert.False(router.Dispatch(NetRegistry.ID_RosterMsg,
            Bytes(w => w.Write(NetConfig.MAX_ARRAY_ELEMENTS + 1))));
        Assert.False(router.Dispatch(NetRegistry.ID_RosterMsg,
            Bytes(w => { w.Write(2); w.Write(123L); })));

        // MatchLoadMsg's config array follows two strings.
        Assert.False(router.Dispatch(NetRegistry.ID_MatchLoadMsg,
            Bytes(w =>
            {
                w.Write("");
                w.Write("");
                w.Write(NetConfig.MAX_BYTE_ARRAY_BYTES + 1);
            })));
        Assert.Empty(roster.Messages);
        Assert.Empty(matchLoad.Messages);
    }

    [Fact]
    public void Dispatch_RejectsOversizedAndMalformedStrings()
    {
        NetRouter router = new();
        ClientProbe<MatchLoadMsg> matchLoad = new();
        router.Add(matchLoad);

        Assert.False(router.Dispatch(NetRegistry.ID_MatchLoadMsg,
            Bytes(w => w.Write(new string('x', NetConfig.MAX_STRING_BYTES + 1)))));
        Assert.False(router.Dispatch(NetRegistry.ID_MatchLoadMsg,
            [0x80, 0x80, 0x80, 0x80, 0x10]));
        Assert.Empty(matchLoad.Messages);
    }

    [Fact]
    public void Dispatch_RejectsEnvelopeAboveCap()
    {
        NetRouter<int> router = new();
        router.Add(new Probe<SetReadyMsg>());
        byte[] oversized = new byte[NetConfig.MAX_ENVELOPE_BYTES + 1];

        Assert.False(router.Dispatch(NetRegistry.ID_SetReadyMsg, SENDER, oversized));
    }

    [Fact]
    public void Dispatch_RandomPayloadsNeverThrow()
    {
        NetRouter<int> router = new();
        router.Add(new RaiseCounter());
        var random = new Random(781_223);
        ushort[] ids = [NetRegistry.ID_SetReadyMsg, NetRegistry.ID_ChatSendMsg,
            NetRegistry.ID_AdminAuthRequestMsg,
            NetRegistry.ID_AdminProofMsg, NetRegistry.ID_LobbyRulesUpdateMsg,
            NetRegistry.ID_LobbyMapUpdateMsg,
            NetRegistry.ID_TeamJoinRequestMsg, NetRegistry.ID_TeamSwapRequestMsg];
        for (int i = 0; i < 10_000; i++)
        {
            byte[] payload = new byte[random.Next(0, 129)];
            random.NextBytes(payload);
            ushort id = ids[random.Next(ids.Length)];
            Exception? error = Record.Exception(() => router.Dispatch(id, SENDER, payload));
            Assert.Null(error);
        }
    }

    /// <summary>Counts anything the router manages to decode and deliver: the
    /// malformed-payload tests need every one of their ids to have a handler,
    /// or the router would refuse them for want of one.</summary>
    private sealed class RaiseCounter :
        IHandle<int, SetReadyMsg>,
        IHandle<int, ChatSendMsg>,
        IHandle<int, AdminAuthRequestMsg>,
        IHandle<int, AdminProofMsg>,
        IHandle<int, LobbyRulesUpdateMsg>,
        IHandle<int, LobbyMapUpdateMsg>,
        IHandle<int, TeamJoinRequestMsg>,
        IHandle<int, TeamSwapRequestMsg>
    {
        public int Count { get; private set; }

        public void Handle(int sender, in SetReadyMsg message) => Count++;

        public void Handle(int sender, in ChatSendMsg message) => Count++;

        public void Handle(int sender, in AdminAuthRequestMsg message) => Count++;

        public void Handle(int sender, in AdminProofMsg message) => Count++;

        public void Handle(int sender, in LobbyRulesUpdateMsg message) => Count++;

        public void Handle(int sender, in LobbyMapUpdateMsg message) => Count++;

        public void Handle(int sender, in TeamJoinRequestMsg message) => Count++;

        public void Handle(int sender, in TeamSwapRequestMsg message) => Count++;
    }

    private static SentEnvelope Capture(ushort id, Action send)
    {
        byte[] payload = [];
        NetTransport.Send = (_, bytes, _, _) => payload = bytes;
        send();
        return new SentEnvelope(id, payload);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }
}
