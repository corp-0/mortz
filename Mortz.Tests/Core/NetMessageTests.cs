using Mortz.Core;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Full protocol round-trips over a loopback NetTransport: send methods ->
/// generated serializer -> NetRegistry.Dispatch -> Received event, no socket.
/// All tests share the NetTransport.Send static, so they live in one class
/// (xunit runs tests within a class sequentially) and restore it after.
/// </summary>
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
            new RosterMsg([1, 77890011223], ["Gilles", "Player 2"]).Broadcast();
        }
        finally
        {
            RosterMsg.Received -= handler;
        }
        Assert.Equal([1, 77890011223], received.PeerIds);
        Assert.Equal(["Gilles", "Player 2"], received.Names);
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
            new LobbyStateMsg([5, 6], ["a", ""], [1, 0]).Broadcast();
        }
        finally
        {
            LobbyStateMsg.Received -= handler;
        }
        Assert.Equal([5, 6], received.PeerIds);
        Assert.Equal(["a", ""], received.Names);
        Assert.Equal([1, 0], received.ReadyFlags);
    }

    [Fact]
    public void WelcomeMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        WelcomeMsg received = default;
        Action<WelcomeMsg> handler = m => received = m;
        WelcomeMsg.Received += handler;
        byte[] config = TestWorlds.Config.ToBytes();
        try
        {
            new WelcomeMsg("castlewars", "abc123", config, [9, 8, 7]).SendTo(5);
        }
        finally
        {
            WelcomeMsg.Received -= handler;
        }
        Assert.Equal("castlewars", received.MapId);
        Assert.Equal("abc123", received.MapHash);
        Assert.Equal(config, received.Config);
        Assert.Equal([9, 8, 7], received.RemovedData);
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
            new DeathMsg(123456789012, -5, 7, true).Broadcast();
        }
        finally
        {
            DeathMsg.Received -= handler;
        }
        Assert.Equal(new DeathMsg(123456789012, -5, 7, true), received);
    }

    [Fact]
    public void ScoreMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        ScoreMsg received = default;
        Action<ScoreMsg> handler = m => received = m;
        ScoreMsg.Received += handler;
        try
        {
            new ScoreMsg(123456789012, 42, -2, 7, 5, 3).Broadcast();
        }
        finally
        {
            ScoreMsg.Received -= handler;
        }
        Assert.Equal(new ScoreMsg(123456789012, 42, -2, 7, 5, 3), received);
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
    public void ClientToServerMsgs_RoundTrip_WithSender()
    {
        UseLoopback(receiverIsServer: true);
        (long Sender, bool Ready) ready = default;
        (long Sender, int X, int Y) carve = default;
        Action<long, SetReadyMsg> readyHandler = (s, m) => ready = (s, m.Ready);
        Action<long, DebugCarveMsg> carveHandler = (s, m) => carve = (s, m.X, m.Y);
        SetReadyMsg.Received += readyHandler;
        DebugCarveMsg.Received += carveHandler;
        try
        {
            new SetReadyMsg(true).SendToServer();
            new DebugCarveMsg(10, 20).SendToServer();
        }
        finally
        {
            SetReadyMsg.Received -= readyHandler;
            DebugCarveMsg.Received -= carveHandler;
        }
        Assert.Equal((SENDER, true), ready);
        Assert.Equal((SENDER, 10, 20), carve);
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
            new RosterMsg([1], ["x"]).Broadcast();
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
        ];

        int raised = 0;
        Action<long, SetReadyMsg> ready = (_, _) => raised++;
        Action<long, DebugCarveMsg> carve = (_, _) => raised++;
        SetReadyMsg.Received += ready;
        DebugCarveMsg.Received += carve;
        try
        {
            foreach ((ushort id, byte[] payload) in messages)
            {
                for (int length = 0; length < payload.Length; length++)
                    Assert.False(NetRegistry.Dispatch(id, SENDER, payload[..length], isServer: true));
                Assert.False(NetRegistry.Dispatch(id, SENDER, [.. payload, 0xA5], isServer: true));
            }
        }
        finally
        {
            SetReadyMsg.Received -= ready;
            DebugCarveMsg.Received -= carve;
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

        // Welcome's first array follows two strings. Its byte-array limit is
        // deliberately larger than collection element limits for terrain masks.
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
        ushort[] ids = [NetRegistry.ID_SetReadyMsg, NetRegistry.ID_DebugCarveMsg];
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
