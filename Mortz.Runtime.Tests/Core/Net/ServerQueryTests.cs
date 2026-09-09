using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ServerQueryTests
{
    public static ServerInfo Sample(string name = "Gilles' Box") =>
        new(name, "Teams", "castlewars", 3, 8, true, true, 7777, 32,
            0xDEADBEEFCAFEUL, 5016960, "0.1.0");

    [Fact]
    public void Requests_MatchStandardWireFixtures()
    {
        Assert.Equal(Convert.FromHexString("FFFFFFFF54536F7572636520456E67696E6520517565727900"),
            ServerQueryProtocol.EncodeInfoRequest());
        Assert.Equal(Convert.FromHexString("FFFFFFFF56FFFFFFFF"), ServerQueryProtocol.EncodeRulesRequest());
        Assert.Equal(Convert.FromHexString("FFFFFFFF4178563412"), ServerQueryProtocol.EncodeChallenge(0x12345678));
        Assert.True(ServerQueryProtocol.TryDecodeRequest(ServerQueryProtocol.EncodeInfoRequest(42), out byte kind, out int? token));
        Assert.Equal(ServerQueryProtocol.INFO_REQUEST, kind);
        Assert.Equal(42, token);
    }

    [Fact]
    public void Info_DecodesIndependentValveStyleFixtureWithAllExtraFields()
    {
        byte[] fixture = Convert.FromHexString(
            "FFFFFFFF49114D79205365727665720064655F64757374320063737472696B6500436F756E7465722D537472696B6500" +
            "0A00030800646C0000312E302E3000F1611E0100000000000000B86952656C6179006B6579776F72647300" +
            "808D4C0000000000");
        Assert.True(ServerQueryProtocol.TryDecodeInfo(fixture, 9999, out ServerInfo info));
        Assert.Equal("My Server", info.Name);
        Assert.Equal("de_dust2", info.Map);
        Assert.Equal(7777, info.GamePort);
        Assert.Equal(5016960u, info.AppId);
        Assert.Equal("1.0.0", info.Version);
        Assert.False(info.MetadataValid);
    }

    [Fact]
    public void InfoAndRules_RoundTripSharedSnapshot()
    {
        ServerInfo original = Sample();
        Assert.True(ServerQueryProtocol.TryDecodeInfo(ServerQueryProtocol.EncodeInfoResponse(original), 1, out ServerInfo info));
        Assert.True(ServerQueryProtocol.TryDecodeRules(ServerQueryProtocol.EncodeRulesResponse(original), out Dictionary<string, string>? rules));
        Assert.Equal(original, ServerQueryMetadata.ApplyRules(info, rules));
        Assert.Equal("0000deadbeefcafe", rules["mortz_schema"]);
    }

    [Fact]
    public void Info_RejectsEveryTruncatedMandatoryFieldAndTrailingGarbage()
    {
        byte[] packet = ServerQueryProtocol.EncodeInfoResponse(Sample());
        for (int length = 0; length < packet.Length - 11; length++)
        {
            Assert.False(ServerQueryProtocol.TryDecodeInfo(packet.AsSpan(0, length), 7777, out _));
        }
        Assert.False(ServerQueryProtocol.TryDecodeInfo([.. packet, 0], 7777, out _));
    }

    [Fact]
    public void Rules_RejectMalformedStringsCountsDuplicatesAndTrailingBytes()
    {
        Assert.False(ServerQueryProtocol.TryDecodeRules(Convert.FromHexString("FFFFFFFF458100"), out _));
        Assert.False(ServerQueryProtocol.TryDecodeRules(Convert.FromHexString("FFFFFFFF4501006B0076"), out _));
        Assert.False(ServerQueryProtocol.TryDecodeRules(Convert.FromHexString("FFFFFFFF4502006B0076006B007700"), out _));
        Assert.False(ServerQueryProtocol.TryDecodeRules(Convert.FromHexString("FFFFFFFF4501006B00FF00"), out _));
        Assert.False(ServerQueryProtocol.TryDecodeRules(Convert.FromHexString("FFFFFFFF45000000"), out _));
        byte[] overlong = [.. Convert.FromHexString("FFFFFFFF4501006B00"), .. new byte[257].Select(_ => (byte)'x'), 0];
        Assert.False(ServerQueryProtocol.TryDecodeRules(overlong, out _));
    }

    [Fact]
    public void Rules_ValueBoundsCountUtf8Bytes()
    {
        var rules = new Dictionary<string, string> { ["k"] = new('é', 128) };
        Assert.True(ServerQueryProtocol.TryDecodeRules(ServerQueryProtocol.EncodeRulesResponse(rules), out _));
        rules["k"] += "é";
        Assert.Throws<ArgumentOutOfRangeException>(() => ServerQueryProtocol.EncodeRulesResponse(rules));
    }

    [Theory]
    [InlineData("mortz_query", "2")]
    [InlineData("mortz_app", "-1")]
    [InlineData("mortz_protocol", " 32")]
    [InlineData("mortz_schema", "deadbeef")]
    [InlineData("mortz_schema", "000000000000000g")]
    [InlineData("mortz_lobby", "true")]
    [InlineData("mortz_join", "2")]
    [InlineData("mortz_mode", "")]
    [InlineData("mortz_mode", "\u202e")]
    [InlineData("mortz_players", "9")]
    public void Metadata_MalformedRequiredValueIsUnknown(string key, string value)
    {
        Dictionary<string, string> rules = ServerQueryMetadata.ToRules(Sample());
        rules[key] = value;
        Assert.False(ServerQueryMetadata.ApplyRules(Sample(), rules).MetadataValid);
    }

    [Fact]
    public void Metadata_EveryRequiredRuleMustBePresent()
    {
        foreach (string key in ServerQueryMetadata.ToRules(Sample()).Keys)
        {
            Dictionary<string, string> rules = ServerQueryMetadata.ToRules(Sample());
            rules.Remove(key);
            Assert.False(ServerQueryMetadata.ApplyRules(Sample(), rules).MetadataValid);
        }
    }

    [Fact]
    public void Endpoints_ValidateDefaultsAndRetainExplicitPorts()
    {
        Assert.Equal(7778, new ServerEndpoint("example.org", 7777).QueryPort);
        Assert.Equal(27015, new ServerEndpoint("example.org", 65535, 27015).QueryPort);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerEndpoint("example.org", 65535));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerEndpoint("example.org", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerEndpoint("example.org", 7777, 65536));
        Assert.Throws<ArgumentException>(() => new ServerEndpoint("example.org", 7777, 7777));
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
