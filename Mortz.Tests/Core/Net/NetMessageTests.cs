using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>
/// Full protocol round-trips over a loopback NetTransport: send methods ->
/// generated serializer -> NetRegistry.Dispatch -> Received event, no socket.
/// All tests share the NetTransport.Send static, so every class that swaps it
/// joins the NetTransport collection (xunit runs a collection sequentially)
/// and restores it after.
/// </summary>
[Collection("NetTransport")]
public class NetMessageTests : IDisposable
{
    private const long SENDER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    /// <summary>Loopback: deliver straight into the given side's dispatch.</summary>
    private static void UseLoopback(bool receiverIsServer) =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, SENDER, payload, receiverIsServer));

    [Fact]
    public void RosterMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        RosterMsg received = default;
        Action<RosterMsg> handler = m => received = m;
        RosterMsg.Received += handler;
        try
        {
            new RosterMsg([1, 77890011223], ["Gilles", "Player 2"], [3, 7], [1, 2], [1, 2]).Broadcast();
        }
        finally
        {
            RosterMsg.Received -= handler;
        }
        Assert.Equal([1, 77890011223], received.PeerIds);
        Assert.Equal(["Gilles", "Player 2"], received.Names);
        Assert.Equal([3, 7], received.Skins);
        Assert.Equal([1, 2], received.Teams);
        Assert.Equal([1, 2], received.Slots);
    }

    [Fact]
    public void PlayerModifiersMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        byte[] blob = ModifierWire.Serialize([Modifiers.Water]);
        PlayerModifiersMsg received = default;
        Action<PlayerModifiersMsg> handler = m => received = m;
        PlayerModifiersMsg.Received += handler;
        try
        {
            new PlayerModifiersMsg(77890011223, blob).Broadcast();
        }
        finally
        {
            PlayerModifiersMsg.Received -= handler;
        }
        Assert.Equal(77890011223, received.PeerId);
        StatsModifier got = Assert.Single(ModifierWire.Deserialize(received.Modifiers));
        Assert.Equal(ModifierId.WATER, got.Id);
        Assert.Equal(Modifiers.Water.Changes, got.Changes);
    }

    [Fact]
    public void LobbyStateMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        LobbyStateMsg received = default;
        Action<LobbyStateMsg> handler = m => received = m;
        LobbyStateMsg.Received += handler;
        try
        {
            new LobbyStateMsg([5, 6], ["a", ""], [1, 0], [1, 2], [5], [6]).Broadcast();
        }
        finally
        {
            LobbyStateMsg.Received -= handler;
        }
        Assert.Equal([5, 6], received.PeerIds);
        Assert.Equal(["a", ""], received.Names);
        Assert.Equal([1, 0], received.ReadyFlags);
        Assert.Equal([1, 2], received.Teams);
        Assert.Equal([5], received.SwapFrom);
        Assert.Equal([6], received.SwapTo);
    }

    [Fact]
    public void ScoreSyncMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        ScoreSyncMsg received = default;
        Action<ScoreSyncMsg> handler = m => received = m;
        ScoreSyncMsg.Received += handler;
        try
        {
            new ScoreSyncMsg([7, 8], [4, -1], [2, 6], 4, -1).SendTo(9);
        }
        finally
        {
            ScoreSyncMsg.Received -= handler;
        }
        Assert.Equal([7, 8], received.PeerIds);
        Assert.Equal([4, -1], received.Kills);
        Assert.Equal([2, 6], received.Deaths);
        Assert.Equal(4, received.Team1Kills);
        Assert.Equal(-1, received.Team2Kills);
    }

    [Fact]
    public void LobbySettingsMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        LobbySettingsMsg received = default;
        Action<LobbySettingsMsg> handler = message => received = message;
        LobbySettingsMsg.Received += handler;
        byte[] config = new MatchConfig { Gravity = 321 }.ToBytes();
        try
        {
            new LobbySettingsMsg("castlewars", "hash", ["arena", "castlewars"],
                ["Arena", "Castle Wars"], config).Broadcast();
        }
        finally
        {
            LobbySettingsMsg.Received -= handler;
        }

        Assert.Equal("castlewars", received.MapId);
        Assert.Equal("hash", received.MapHash);
        Assert.Equal(["arena", "castlewars"], received.MapIds);
        Assert.Equal(["Arena", "Castle Wars"], received.MapNames);
        Assert.Equal(config, received.Config);
    }

    [Fact]
    public void WelcomeMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        WelcomeMsg received = default;
        Action<WelcomeMsg> handler = m => received = m;
        WelcomeMsg.Received += handler;
        byte[] config = TestWorlds.NoSpawnProtectionConfig.ToBytes();
        try
        {
            new WelcomeMsg("castlewars", "abc123", config,
                (byte)TerrainSyncEncoding.CARVE_LOG, 17, 12345, 2).SendTo(5);
        }
        finally
        {
            WelcomeMsg.Received -= handler;
        }
        Assert.Equal("castlewars", received.MapId);
        Assert.Equal("abc123", received.MapHash);
        Assert.Equal(config, received.Config);
        Assert.Equal((byte)TerrainSyncEncoding.CARVE_LOG, received.TerrainEncoding);
        Assert.Equal(17, received.TerrainTransferId);
        Assert.Equal(12345, received.TerrainBytes);
        Assert.Equal(2, received.TerrainChunks);
    }

    [Fact]
    public void CarveMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        CarveMsg received = default;
        Action<CarveMsg> handler = m => received = m;
        CarveMsg.Received += handler;
        try
        {
            new CarveMsg(1986, 972, 12, 1646958266, -1).Broadcast();
        }
        finally
        {
            CarveMsg.Received -= handler;
        }
        Assert.Equal(new CarveMsg(1986, 972, 12, 1646958266, -1), received);
    }

    [Fact]
    public void ShellRetireMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        ShellRetireMsg received = default;
        Action<ShellRetireMsg> handler = m => received = m;
        ShellRetireMsg.Received += handler;
        try
        {
            new ShellRetireMsg(314).SendTo(7);
        }
        finally
        {
            ShellRetireMsg.Received -= handler;
        }
        Assert.Equal(new ShellRetireMsg(314), received);
    }

    [Fact]
    public void DeathMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        DeathMsg received = default;
        Action<DeathMsg> handler = m => received = m;
        DeathMsg.Received += handler;
        try
        {
            new DeathMsg(1234567890, -5, 7).Broadcast();
        }
        finally
        {
            DeathMsg.Received -= handler;
        }
        Assert.Equal(new DeathMsg(1234567890, -5, 7), received);
    }

    [Fact]
    public void EliminationMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        EliminationMsg received = default;
        Action<EliminationMsg> handler = m => received = m;
        EliminationMsg.Received += handler;
        try
        {
            new EliminationMsg(123456789012, 42,
                EliminationFlags.FIRST_BLOOD | EliminationFlags.OWNED,
                -2, 7, 5, 3).Broadcast();
        }
        finally
        {
            EliminationMsg.Received -= handler;
        }
        Assert.Equal(new EliminationMsg(123456789012, 42,
            EliminationFlags.FIRST_BLOOD | EliminationFlags.OWNED,
            -2, 7, 5, 3), received);
    }

    [Fact]
    public void MatchEndMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        MatchEndMsg received = default;
        Action<MatchEndMsg> handler = m => received = m;
        MatchEndMsg.Received += handler;
        try
        {
            new MatchEndMsg(true, 2).Broadcast();
        }
        finally
        {
            MatchEndMsg.Received -= handler;
        }
        Assert.Equal(new MatchEndMsg(true, 2), received);
    }

    [Fact]
    public void ChatAndAdminServerMessages_RoundTrip()
    {
        UseLoopback(receiverIsServer: false);
        ChatLineMsg line = default;
        AdminChallengeMsg challenge = default;
        AdminStateMsg state = default;
        Action<ChatLineMsg> lineHandler = message => line = message;
        Action<AdminChallengeMsg> challengeHandler = message => challenge = message;
        Action<AdminStateMsg> stateHandler = message => state = message;
        ChatLineMsg.Received += lineHandler;
        AdminChallengeMsg.Received += challengeHandler;
        AdminStateMsg.Received += stateHandler;
        try
        {
            new ChatLineMsg(ChatLineKind.PLAYER, 42, "Alice", "hello 🐛").Broadcast();
            new AdminChallengeMsg([1, 2, 3]).SendTo(42);
            new AdminStateMsg(true, "granted").SendTo(42);
        }
        finally
        {
            ChatLineMsg.Received -= lineHandler;
            AdminChallengeMsg.Received -= challengeHandler;
            AdminStateMsg.Received -= stateHandler;
        }
        Assert.Equal(new ChatLineMsg(ChatLineKind.PLAYER, 42, "Alice", "hello 🐛"), line);
        Assert.Equal([1, 2, 3], challenge.Challenge);
        Assert.Equal(new AdminStateMsg(true, "granted"), state);
    }

    [Fact]
    public void FinalKillMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        FinalKillMsg received = default;
        Action<FinalKillMsg> handler = m => received = m;
        FinalKillMsg.Received += handler;
        FinalKillMsg expected = new(
            781, 12, 42,
            FinalKillFlags.EXPLOSION | FinalKillFlags.OWNED,
            -5, 700, 20, 680, 48);
        try
        {
            expected.Broadcast();
        }
        finally
        {
            FinalKillMsg.Received -= handler;
        }
        Assert.Equal(expected, received);
    }

    [Fact]
    public void ClientToServerMsgs_RoundTrip_WithSender()
    {
        UseLoopback(receiverIsServer: true);
        (long Sender, bool Ready) ready = default;
        (long Sender, int X, int Y) carve = default;
        (long Sender, byte Team) join = default;
        Action<long, SetReadyMsg> readyHandler = (s, m) => ready = (s, m.Ready);
        Action<long, DebugCarveMsg> carveHandler = (s, m) => carve = (s, m.X, m.Y);
        Action<long, TeamJoinRequestMsg> joinHandler = (s, m) => join = (s, m.Team);
        SetReadyMsg.Received += readyHandler;
        DebugCarveMsg.Received += carveHandler;
        TeamJoinRequestMsg.Received += joinHandler;
        try
        {
            new SetReadyMsg(true).SendToServer();
            new DebugCarveMsg(10, 20).SendToServer();
            new TeamJoinRequestMsg(2).SendToServer();
        }
        finally
        {
            SetReadyMsg.Received -= readyHandler;
            DebugCarveMsg.Received -= carveHandler;
            TeamJoinRequestMsg.Received -= joinHandler;
        }
        Assert.Equal((SENDER, true), ready);
        Assert.Equal((SENDER, 10, 20), carve);
        Assert.Equal((SENDER, (byte)2), join);
    }

    [Fact]
    public void ChatAndAdminClientMessages_RoundTrip_WithTransportSender()
    {
        UseLoopback(receiverIsServer: true);
        (long Sender, string Text) chat = default;
        long requestSender = 0;
        (long Sender, byte[] Proof) proof = default;
        Action<long, ChatSendMsg> chatHandler = (sender, message) => chat = (sender, message.Text);
        Action<long, AdminAuthRequestMsg> requestHandler = (sender, _) => requestSender = sender;
        Action<long, AdminProofMsg> proofHandler = (sender, message) => proof = (sender, message.Proof);
        ChatSendMsg.Received += chatHandler;
        AdminAuthRequestMsg.Received += requestHandler;
        AdminProofMsg.Received += proofHandler;
        try
        {
            new ChatSendMsg("hello").SendToServer();
            new AdminAuthRequestMsg().SendToServer();
            new AdminProofMsg([7, 8, 9]).SendToServer();
        }
        finally
        {
            ChatSendMsg.Received -= chatHandler;
            AdminAuthRequestMsg.Received -= requestHandler;
            AdminProofMsg.Received -= proofHandler;
        }
        Assert.Equal((SENDER, "hello"), chat);
        Assert.Equal(SENDER, requestSender);
        Assert.Equal(SENDER, proof.Sender);
        Assert.Equal([7, 8, 9], proof.Proof);
    }

    [Fact]
    public void SignedLobbyUpdates_RoundTrip_WithUnsignedSequenceBitsIntact()
    {
        UseLoopback(receiverIsServer: true);
        (long Sender, LobbyRulesUpdateMsg Message) rules = default;
        (long Sender, LobbyMapUpdateMsg Message) map = default;
        Action<long, LobbyRulesUpdateMsg> rulesHandler =
            (sender, message) => rules = (sender, message);
        Action<long, LobbyMapUpdateMsg> mapHandler =
            (sender, message) => map = (sender, message);
        LobbyRulesUpdateMsg.Received += rulesHandler;
        LobbyMapUpdateMsg.Received += mapHandler;
        try
        {
            new LobbyRulesUpdateMsg([1, 2], ulong.MaxValue, [3, 4]).SendToServer();
            new LobbyMapUpdateMsg("arena", ulong.MaxValue - 1, [5, 6]).SendToServer();
        }
        finally
        {
            LobbyRulesUpdateMsg.Received -= rulesHandler;
            LobbyMapUpdateMsg.Received -= mapHandler;
        }

        Assert.Equal(SENDER, rules.Sender);
        Assert.Equal([1, 2], rules.Message.Config);
        Assert.Equal(ulong.MaxValue, rules.Message.Sequence);
        Assert.Equal([3, 4], rules.Message.Tag);
        Assert.Equal(SENDER, map.Sender);
        Assert.Equal("arena", map.Message.MapId);
        Assert.Equal(ulong.MaxValue - 1, map.Message.Sequence);
        Assert.Equal([5, 6], map.Message.Tag);
    }

    [Fact]
    public void LobbySettingsRequest_RoundTrips_WithTransportSender()
    {
        UseLoopback(receiverIsServer: true);
        long sender = 0;
        Action<long, LobbySettingsRequestMsg> handler = (peerId, _) => sender = peerId;
        LobbySettingsRequestMsg.Received += handler;
        try
        {
            new LobbySettingsRequestMsg().SendToServer();
        }
        finally
        {
            LobbySettingsRequestMsg.Received -= handler;
        }
        Assert.Equal(SENDER, sender);
    }

    [Fact]
    public void RollRequestMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: true);
        long sender = 0;
        Action<long, RollRequestMsg> handler = (peerId, _) => sender = peerId;
        RollRequestMsg.Received += handler;
        try
        {
            new RollRequestMsg().SendToServer();
        }
        finally
        {
            RollRequestMsg.Received -= handler;
        }
        Assert.Equal(SENDER, sender);
    }

    [Fact]
    public void Dispatch_DropsWrongDirection()
    {
        // A client-only message arriving at the server (spoof) and vice versa.
        byte[] captured = [];
        NetTransport.Send = (_, payload, _, _) => captured = payload;
        bool raised = false;
        Action<RosterMsg> rosterHandler = _ => raised = true;
        Action<long, SetReadyMsg> readyHandler = (_, _) => raised = true;
        RosterMsg.Received += rosterHandler;
        SetReadyMsg.Received += readyHandler;
        try
        {
            new RosterMsg([1], ["x"], [2], [0], [1]).Broadcast();
            Assert.False(NetRegistry.Dispatch(NetRegistry.ID_RosterMsg, SENDER, captured, isServer: true));
            new SetReadyMsg(true).SendToServer();
            Assert.False(NetRegistry.Dispatch(NetRegistry.ID_SetReadyMsg, SENDER, captured, isServer: false));
        }
        finally
        {
            RosterMsg.Received -= rosterHandler;
            SetReadyMsg.Received -= readyHandler;
        }
        Assert.False(raised);
    }

    [Fact]
    public void Dispatch_DropsUnknownId()
    {
        Assert.False(NetRegistry.Dispatch(ushort.MaxValue, SENDER, [], isServer: false));
    }

    [Fact]
    public void Dispatch_RejectsEveryClientMessageTruncationAndTrailingBytes()
    {
        (ushort Id, byte[] Payload)[] messages = [
            Capture(NetRegistry.ID_SetReadyMsg, () => new SetReadyMsg(true).SendToServer()),
            Capture(NetRegistry.ID_DebugCarveMsg, () => new DebugCarveMsg(10, 20).SendToServer()),
            Capture(NetRegistry.ID_ChatSendMsg, () => new ChatSendMsg("hello").SendToServer()),
            Capture(NetRegistry.ID_AdminAuthRequestMsg, () => new AdminAuthRequestMsg().SendToServer()),
            Capture(NetRegistry.ID_AdminProofMsg, () => new AdminProofMsg([1, 2, 3]).SendToServer()),
            Capture(NetRegistry.ID_LobbyRulesUpdateMsg,
                () => new LobbyRulesUpdateMsg([1, 2], 3, [4, 5]).SendToServer()),
            Capture(NetRegistry.ID_LobbyMapUpdateMsg,
                () => new LobbyMapUpdateMsg("arena", 3, [4, 5]).SendToServer()),
            Capture(NetRegistry.ID_LobbySettingsRequestMsg,
                () => new LobbySettingsRequestMsg().SendToServer()),
            Capture(NetRegistry.ID_TeamJoinRequestMsg,
                () => new TeamJoinRequestMsg(1).SendToServer()),
            Capture(NetRegistry.ID_TeamSwapRequestMsg,
                () => new TeamSwapRequestMsg(42).SendToServer()),
        ];

        int raised = 0;
        Action<long, SetReadyMsg> ready = (_, _) => raised++;
        Action<long, DebugCarveMsg> carve = (_, _) => raised++;
        Action<long, ChatSendMsg> chat = (_, _) => raised++;
        Action<long, AdminAuthRequestMsg> request = (_, _) => raised++;
        Action<long, AdminProofMsg> proof = (_, _) => raised++;
        Action<long, LobbyRulesUpdateMsg> rules = (_, _) => raised++;
        Action<long, LobbyMapUpdateMsg> map = (_, _) => raised++;
        Action<long, LobbySettingsRequestMsg> settingsRequest = (_, _) => raised++;
        Action<long, TeamJoinRequestMsg> teamJoin = (_, _) => raised++;
        Action<long, TeamSwapRequestMsg> teamSwap = (_, _) => raised++;
        SetReadyMsg.Received += ready;
        DebugCarveMsg.Received += carve;
        ChatSendMsg.Received += chat;
        AdminAuthRequestMsg.Received += request;
        AdminProofMsg.Received += proof;
        LobbyRulesUpdateMsg.Received += rules;
        LobbyMapUpdateMsg.Received += map;
        LobbySettingsRequestMsg.Received += settingsRequest;
        TeamJoinRequestMsg.Received += teamJoin;
        TeamSwapRequestMsg.Received += teamSwap;
        try
        {
            foreach ((ushort id, byte[] payload) in messages)
            {
                for (int length = 0; length < payload.Length; length++)
                {
                    Assert.False(NetRegistry.Dispatch(id, SENDER, payload[..length], isServer: true));
                }
                Assert.False(NetRegistry.Dispatch(id, SENDER, [.. payload, 0xA5], isServer: true));
            }
        }
        finally
        {
            SetReadyMsg.Received -= ready;
            DebugCarveMsg.Received -= carve;
            ChatSendMsg.Received -= chat;
            AdminAuthRequestMsg.Received -= request;
            AdminProofMsg.Received -= proof;
            LobbyRulesUpdateMsg.Received -= rules;
            LobbyMapUpdateMsg.Received -= map;
            LobbySettingsRequestMsg.Received -= settingsRequest;
            TeamJoinRequestMsg.Received -= teamJoin;
            TeamSwapRequestMsg.Received -= teamSwap;
        }
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Dispatch_RejectsNegativeHugeAndTruncatedArrayLengths()
    {
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_RosterMsg, SENDER,
            Bytes(w => w.Write(-1)), isServer: false));
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_RosterMsg, SENDER,
            Bytes(w => w.Write(NetConfig.MAX_ARRAY_ELEMENTS + 1)), isServer: false));
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_RosterMsg, SENDER,
            Bytes(w => { w.Write(2); w.Write(123L); }), isServer: false));

        // Welcome's config array follows two strings.
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_WelcomeMsg, SENDER,
            Bytes(w =>
            {
                w.Write("");
                w.Write("");
                w.Write(NetConfig.MAX_BYTE_ARRAY_BYTES + 1);
            }), isServer: false));
    }

    [Fact]
    public void Dispatch_RejectsOversizedAndMalformedStrings()
    {
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_WelcomeMsg, SENDER,
            Bytes(w => w.Write(new string('x', NetConfig.MAX_STRING_BYTES + 1))), isServer: false));
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_WelcomeMsg, SENDER,
            [0x80, 0x80, 0x80, 0x80, 0x10], isServer: false));
    }

    [Fact]
    public void Dispatch_RejectsEnvelopeAboveCap()
    {
        byte[] oversized = new byte[NetConfig.MAX_ENVELOPE_BYTES + 1];
        Assert.False(NetRegistry.Dispatch(NetRegistry.ID_SetReadyMsg, SENDER, oversized, isServer: true));
    }

    [Fact]
    public void Dispatch_RandomPayloadsNeverThrow()
    {
        var random = new Random(781_223);
        ushort[] ids = [NetRegistry.ID_SetReadyMsg, NetRegistry.ID_DebugCarveMsg,
            NetRegistry.ID_ChatSendMsg, NetRegistry.ID_AdminAuthRequestMsg,
            NetRegistry.ID_AdminProofMsg, NetRegistry.ID_LobbyRulesUpdateMsg,
            NetRegistry.ID_LobbyMapUpdateMsg, NetRegistry.ID_LobbySettingsRequestMsg,
            NetRegistry.ID_TeamJoinRequestMsg, NetRegistry.ID_TeamSwapRequestMsg];
        for (int i = 0; i < 10_000; i++)
        {
            byte[] payload = new byte[random.Next(0, 129)];
            random.NextBytes(payload);
            ushort id = ids[random.Next(ids.Length)];
            Exception? error = Record.Exception(() =>
                NetRegistry.Dispatch(id, SENDER, payload, isServer: true));
            Assert.Null(error);
        }
    }

    private static (ushort Id, byte[] Payload) Capture(ushort id, Action send)
    {
        byte[] payload = [];
        NetTransport.Send = (_, bytes, _, _) => payload = bytes;
        send();
        return (id, payload);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }
}
