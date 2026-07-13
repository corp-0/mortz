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
    public void DeathMsg_RoundTrips()
    {
        UseLoopback(receiverIsServer: false);
        DeathMsg received = default;
        Action<DeathMsg> handler = m => received = m;
        DeathMsg.Received += handler;
        try
        {
            new DeathMsg(123456789012, -5, 7).Broadcast();
        }
        finally
        {
            DeathMsg.Received -= handler;
        }
        Assert.Equal(new DeathMsg(123456789012, -5, 7), received);
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
}
