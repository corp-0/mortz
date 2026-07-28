using System.Text;
using Mortz.Core.Net.Query;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class ServerQueryTests
{
    private static ServerInfo Sample(string name = "Gilles' Box") =>
        new(name, "Teams - Team Kills", "castlewars", Players: 3, MaxPlayers: 8,
            InLobby: true, GamePort: 7777, ProtocolVersion: 32, SchemaHash: 0xDEADBEEFCAFEUL);

    [Fact]
    public void Request_RoundTripsNonceAndIsPadded()
    {
        byte[] datagram = ServerQueryProtocol.EncodeRequest(0xA1B2C3D4);

        Assert.Equal(ServerQueryProtocol.REQUEST_BYTES, datagram.Length);
        Assert.True(ServerQueryProtocol.TryDecodeRequest(datagram, out uint nonce));
        Assert.Equal(0xA1B2C3D4u, nonce);
    }

    [Fact]
    public void Response_RoundTripsEveryField()
    {
        ServerInfo info = Sample();

        byte[] datagram = ServerQueryProtocol.EncodeResponse(7, info);

        Assert.True(datagram.Length <= ServerQueryProtocol.MAX_RESPONSE_BYTES);
        Assert.True(ServerQueryProtocol.TryDecodeResponse(datagram, out ServerQueryReply reply));
        Assert.Equal(7u, reply.Nonce);
        Assert.Equal(info, reply.Info);
    }

    [Fact]
    public void Response_TruncatesOverlongText()
    {
        string name = new('x', ServerQueryProtocol.MAX_TEXT_LENGTH + 40);

        byte[] datagram = ServerQueryProtocol.EncodeResponse(1, Sample(name));

        Assert.True(ServerQueryProtocol.TryDecodeResponse(datagram, out ServerQueryReply reply));
        Assert.Equal(ServerQueryProtocol.MAX_TEXT_LENGTH, reply.Info.Name.Length);
    }

    /// <summary>Hand-built, not round-tripped: a modified server will not run
    /// our encoder, so decode has to sanitize on its own.</summary>
    [Fact]
    public void Response_SanitizesHostileTextOnDecode()
    {
        string hostile = "evil\r\nname\u202E";
        string pairSplitter = new string('x', ServerQueryProtocol.MAX_TEXT_LENGTH - 1) + "😀";

        byte[] datagram = RawResponse(9, hostile, "mode", pairSplitter);

        Assert.True(ServerQueryProtocol.TryDecodeResponse(datagram, out ServerQueryReply reply));
        Assert.Equal("evilname", reply.Info.Name);
        // The emoji straddles the cap; rune truncation drops it whole instead
        // of leaving a lone surrogate.
        Assert.Equal(new string('x', ServerQueryProtocol.MAX_TEXT_LENGTH - 1), reply.Info.Map);
    }

    private static byte[] RawResponse(uint nonce, string name, string mode, string map)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("MZQ1"u8.ToArray());
        writer.Write(ServerQueryProtocol.VERSION);
        writer.Write((byte)2); // KIND_RESPONSE
        writer.Write(nonce);
        writer.Write(32); // protocol version
        writer.Write(0xFEEDUL); // schema hash
        writer.Write((ushort)7777);
        writer.Write((byte)3);
        writer.Write((byte)8);
        writer.Write(true);
        writer.Write(name);
        writer.Write(mode);
        writer.Write(map);
        return stream.ToArray();
    }

    [Fact]
    public void Request_RejectsUnpaddedDatagram()
    {
        byte[] datagram = ServerQueryProtocol.EncodeRequest(1)[..16];

        Assert.False(ServerQueryProtocol.TryDecodeRequest(datagram, out _));
    }

    [Fact]
    public void Request_RejectsForeignMagicAndWrongKind()
    {
        byte[] foreign = ServerQueryProtocol.EncodeRequest(1);
        foreign[0] = (byte)'X';
        byte[] response = ServerQueryProtocol.EncodeResponse(1, Sample());

        Assert.False(ServerQueryProtocol.TryDecodeRequest(foreign, out _));
        Assert.False(ServerQueryProtocol.TryDecodeRequest(response, out _));
        Assert.False(ServerQueryProtocol.TryDecodeResponse(
            ServerQueryProtocol.EncodeRequest(1), out _));
    }

    [Fact]
    public void Response_RejectsTruncatedPayload()
    {
        byte[] datagram = ServerQueryProtocol.EncodeResponse(1, Sample());

        Assert.False(ServerQueryProtocol.TryDecodeResponse(datagram[..(datagram.Length - 4)], out _));
    }

    [Fact]
    public void Response_RejectsOversizedDatagram()
    {
        byte[] datagram = new byte[ServerQueryProtocol.MAX_RESPONSE_BYTES + 1];
        ServerQueryProtocol.EncodeResponse(1, Sample()).CopyTo(datagram, 0);

        Assert.False(ServerQueryProtocol.TryDecodeResponse(datagram, out _));
    }

    [Fact]
    public void QueryPort_SitsOneAboveTheGamePort()
    {
        Assert.Equal(7778, ServerQueryProtocol.QueryPort(7777));
    }

    [Fact]
    public void RateLimiter_AllowsBurstThenRefills()
    {
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 4, globalPerSecond: 100);

        int allowed = 0;
        for (int i = 0; i < 20; i++)
        {
            if (limiter.Allow("1.2.3.4", 0))
                allowed++;
        }

        Assert.Equal(8, allowed); // 4/s over a 2 s burst window
        Assert.False(limiter.Allow("1.2.3.4", 100));
        Assert.True(limiter.Allow("1.2.3.4", 1_000));
    }

    [Fact]
    public void RateLimiter_KeepsSourcesIndependent()
    {
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 1, globalPerSecond: 100);

        while (limiter.Allow("1.2.3.4", 0))
        {
        }

        Assert.True(limiter.Allow("5.6.7.8", 0));
    }

    [Fact]
    public void RateLimiter_CapsTotalRepliesAcrossSources()
    {
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 10, globalPerSecond: 5);

        int allowed = 0;
        for (int source = 0; source < 20; source++)
        {
            if (limiter.Allow($"10.0.0.{source}", 0))
                allowed++;
        }

        Assert.Equal(10, allowed); // 5/s over the same 2 s burst window
    }

    /// <summary>A spoofed flood fills the table with fresh buckets in
    /// milliseconds, so the limiter has to evict rather than lock out every
    /// unseen source.</summary>
    [Fact]
    public void RateLimiter_FullTableEvictsTheLeastRecentlySeenSource()
    {
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 1, globalPerSecond: 1_000_000,
            maxSources: 4);

        Assert.True(limiter.Allow("10.0.0.1", 0));
        // Drain the second source so its retained bucket is observable.
        Assert.True(limiter.Allow("10.0.0.2", 1));
        Assert.True(limiter.Allow("10.0.0.2", 1));
        Assert.False(limiter.Allow("10.0.0.2", 1));
        Assert.True(limiter.Allow("10.0.0.3", 2));
        Assert.True(limiter.Allow("10.0.0.4", 3));

        // Table full of fresh buckets, yet a new source is still admitted.
        Assert.True(limiter.Allow("10.0.1.1", 4));
        // The drained bucket was not the one evicted, so it still denies.
        Assert.False(limiter.Allow("10.0.0.2", 5));
    }

    [Fact]
    public void RateLimiter_SourceDeniedByTheGlobalCapKeepsItsOwnToken()
    {
        // perSource 0.5 gives each source exactly one token in its burst
        // window, so a wrongly burned token is visible.
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 0.5, globalPerSecond: 2);

        for (int source = 0; source < 4; source++)
        {
            Assert.True(limiter.Allow($"10.0.0.{source}", 0));
        }
        Assert.False(limiter.Allow("10.0.1.1", 0)); // global pool is dry

        // Half a second refills the global pool.
        Assert.True(limiter.Allow("10.0.1.1", 500));
    }

    [Fact]
    public void RateLimiter_OneFloodingSourceCannotDrainTheSharedPool()
    {
        ServerQueryRateLimiter limiter = new(perSourcePerSecond: 4, globalPerSecond: 10);

        int allowed = 0;
        for (int i = 0; i < 1_000; i++)
        {
            if (limiter.Allow("6.6.6.6", 0))
                allowed++;
        }

        Assert.Equal(8, allowed); // its own 2 s burst, nothing more
        // Another source still gets its full burst from the shared pool.
        for (int i = 0; i < 8; i++)
        {
            Assert.True(limiter.Allow("1.2.3.4", 0));
        }
    }
}
