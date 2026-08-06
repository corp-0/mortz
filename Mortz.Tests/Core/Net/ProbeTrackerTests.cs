using Mortz.Core.Net.Query;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class ProbeTrackerTests
{
    private static readonly ServerEndpoint _endpoint = new("9.9.9.9", 7777);

    private static ServerInfo Sample() =>
        new("Gilles' Box", "Teams", "castlewars", Players: 3, MaxPlayers: 8,
            InLobby: true, AllowJoinInProgress: true, GamePort: 7777,
            ProtocolVersion: 32, SchemaHash: 0xFEEDUL);

    private static ProbeTracker Sequenced(params uint[] nonces)
    {
        Queue<uint> queue = new(nonces);
        return new ProbeTracker(queue.Dequeue);
    }

    private static ProbeTracker Counting()
    {
        uint next = 0;
        return new ProbeTracker(() => ++next);
    }

    [Fact]
    public void Nonces_SkipZeroAndCollisions()
    {
        ProbeTracker tracker = Sequenced(0, 5, 5, 7);

        Assert.Equal(5u, tracker.Track(_endpoint, 0));
        Assert.Equal(7u, tracker.Track(new ServerEndpoint("8.8.8.8", 7777), 0));
    }

    [Fact]
    public void Reply_FiresOnceAndRemovesThePending()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> replies = [];
        List<ServerEndpoint> timeouts = [];
        tracker.Replied += replies.Add;
        tracker.TimedOut += (_, endpoint) => timeouts.Add(endpoint);
        uint nonce = tracker.Track(_endpoint, 0);
        tracker.MarkSent(nonce, _endpoint.Address, 0);
        byte[] datagram = ServerQueryProtocol.EncodeResponse(nonce, Sample());

        tracker.OnResponse(datagram, _endpoint.Address, _endpoint.QueryPort, 120);
        tracker.OnResponse(datagram, _endpoint.Address, _endpoint.QueryPort, 130);
        tracker.Expire(60_000);

        ServerProbeReply reply = Assert.Single(replies);
        Assert.Equal(_endpoint, reply.Endpoint);
        Assert.Equal(120, reply.PingMs);
        Assert.Empty(timeouts);
    }

    /// <summary>A matching nonce is not enough: the answer has to come from
    /// the address we probed.</summary>
    [Fact]
    public void Reply_FromTheWrongAddressIsIgnoredAndThePendingStillExpires()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> replies = [];
        List<ServerEndpoint> timeouts = [];
        tracker.Replied += replies.Add;
        tracker.TimedOut += (_, endpoint) => timeouts.Add(endpoint);
        uint nonce = tracker.Track(_endpoint, 0);
        tracker.MarkSent(nonce, _endpoint.Address, 0);
        byte[] datagram = ServerQueryProtocol.EncodeResponse(nonce, Sample());

        tracker.OnResponse(datagram, "6.6.6.6", _endpoint.QueryPort, 100);
        tracker.OnResponse(datagram, _endpoint.Address, _endpoint.QueryPort + 1, 100);

        Assert.Empty(replies);
        tracker.Expire(ProbeTracker.TIMEOUT_MS + 1);
        Assert.Equal([_endpoint], timeouts);
    }

    [Fact]
    public void Reply_ToAnUnsentNonceIsIgnored()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> replies = [];
        tracker.Replied += replies.Add;
        uint nonce = tracker.Track(_endpoint, 0);

        tracker.OnResponse(ServerQueryProtocol.EncodeResponse(nonce, Sample()),
            _endpoint.Address, _endpoint.QueryPort, 100);

        Assert.Empty(replies);
    }

    [Fact]
    public void Timeout_FiresExactlyOnce()
    {
        ProbeTracker tracker = Counting();
        List<ServerEndpoint> timeouts = [];
        tracker.TimedOut += (_, endpoint) => timeouts.Add(endpoint);
        uint nonce = tracker.Track(_endpoint, 0);
        tracker.MarkSent(nonce, _endpoint.Address, 0);

        tracker.Expire(ProbeTracker.TIMEOUT_MS);
        Assert.Empty(timeouts);
        tracker.Expire(ProbeTracker.TIMEOUT_MS + 1);
        tracker.Expire(ProbeTracker.TIMEOUT_MS + 2);

        Assert.Equal([_endpoint], timeouts);
    }

    [Fact]
    public void Fail_ReportsTheTimeoutImmediately()
    {
        ProbeTracker tracker = Counting();
        List<ServerEndpoint> timeouts = [];
        tracker.TimedOut += (_, endpoint) => timeouts.Add(endpoint);
        uint nonce = tracker.Track(_endpoint, 0);

        tracker.Fail(nonce);
        tracker.Fail(nonce);
        tracker.Expire(60_000);

        Assert.Equal([_endpoint], timeouts);
    }

    [Fact]
    public void Broadcast_AcceptsInsideTheWindowFromTheConventionalPort()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> discovered = [];
        tracker.Discovered += discovered.Add;
        uint nonce = tracker.BeginBroadcast(0, 7778);

        tracker.OnResponse(ServerQueryProtocol.EncodeResponse(nonce, Sample()),
            "192.168.1.5", 7778, 500);

        ServerProbeReply reply = Assert.Single(discovered);
        Assert.Equal(new ServerEndpoint("192.168.1.5", 7777), reply.Endpoint);
        Assert.Equal(500, reply.PingMs);
    }

    [Fact]
    public void Broadcast_IgnoresLateAndWrongPortReplies()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> discovered = [];
        tracker.Discovered += discovered.Add;
        uint nonce = tracker.BeginBroadcast(0, 7778);
        byte[] datagram = ServerQueryProtocol.EncodeResponse(nonce, Sample());

        tracker.OnResponse(datagram, "192.168.1.5", 7778, ProbeTracker.TIMEOUT_MS + 1);
        tracker.OnResponse(datagram, "192.168.1.5", 9999, 500);

        Assert.Empty(discovered);
    }

    [Fact]
    public void Cancel_SilencesEverythingInFlight()
    {
        ProbeTracker tracker = Counting();
        List<ServerProbeReply> replies = [];
        List<ServerEndpoint> timeouts = [];
        tracker.Replied += replies.Add;
        tracker.TimedOut += (_, endpoint) => timeouts.Add(endpoint);
        uint nonce = tracker.Track(_endpoint, 0);
        tracker.MarkSent(nonce, _endpoint.Address, 0);

        tracker.Cancel();
        tracker.OnResponse(ServerQueryProtocol.EncodeResponse(nonce, Sample()),
            _endpoint.Address, _endpoint.QueryPort, 100);
        tracker.Expire(60_000);

        Assert.Empty(replies);
        Assert.Empty(timeouts);
    }
}
