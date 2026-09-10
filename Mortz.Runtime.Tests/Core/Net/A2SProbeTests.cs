using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class A2SProbeTests
{
    private static readonly ServerEndpoint _endpoint = new("server.example", 7777, 27015);
    private const string ADDRESS = "192.0.2.1";

    [Fact]
    public void Probe_CompletesBothChallengedExchangesAndRetainsQueryEndpoint()
    {
        A2SProbe probe = new(_endpoint, ADDRESS, 0);
        A2SResponder responder = new();
        byte[]? request = probe.StartRequest();
        int exchanges = 0;
        while (request != null)
        {
            byte[] response = Assert.Single(responder.Respond(request, "192.0.2.2", 45000,
                (ulong)exchanges * 10, ServerQueryTests.Sample()));
            request = probe.Receive(response, ADDRESS, _endpoint.QueryPort, (ulong)++exchanges * 10);
        }
        Assert.Equal(4, exchanges);
        Assert.True(probe.IsComplete);
        Assert.Equal(ServerQueryTests.Sample(), probe.Result!.Value.Info);
        Assert.Equal(_endpoint, probe.Result.Value.Endpoint);
        Assert.Equal(10, probe.Result.Value.PingMs);
        Assert.Null(probe.Receive(ServerQueryProtocol.EncodeChallenge(1), ADDRESS, _endpoint.QueryPort, 41));
    }

    [Fact]
    public void Probe_IgnoresWrongSenderAndReceiptsOutsideDeadline()
    {
        A2SProbe probe = new(_endpoint, ADDRESS, 100);
        probe.StartRequest();
        byte[] info = ServerQueryProtocol.EncodeInfoResponse(ServerQueryTests.Sample());
        Assert.Null(probe.Receive(info, "192.0.2.3", _endpoint.QueryPort, 200));
        Assert.Null(probe.Receive(info, ADDRESS, _endpoint.QueryPort + 1, 200));
        Assert.Null(probe.Receive(info, ADDRESS, _endpoint.QueryPort, 3100));
        Assert.True(probe.HasExpired(3100));
        probe.Finish();
        Assert.Null(probe.Result);
    }

    [Fact]
    public void Probe_MissingRulesKeepInfoWithUnknownCompatibility()
    {
        A2SProbe probe = new(_endpoint, ADDRESS, 0);
        probe.StartRequest();
        Assert.NotNull(probe.Receive(ServerQueryProtocol.EncodeInfoResponse(ServerQueryTests.Sample()), ADDRESS, _endpoint.QueryPort, 25));
        Assert.True(probe.HasExpired(3000));
        probe.Finish();
        Assert.True(probe.IsComplete);
        Assert.Equal("Gilles' Box", probe.Result!.Value.Info.Name);
        Assert.False(probe.Result.Value.Info.MetadataValid);
        Assert.Equal(25, probe.Result.Value.PingMs);
    }

    [Fact]
    public void Probe_ChallengeRetriesAreBoundedPerRequest()
    {
        A2SProbe probe = new(_endpoint, ADDRESS, 0);
        probe.StartRequest();
        byte[] challenge = ServerQueryProtocol.EncodeChallenge(42);
        Assert.NotNull(probe.Receive(challenge, ADDRESS, _endpoint.QueryPort, 10));
        Assert.NotNull(probe.Receive(challenge, ADDRESS, _endpoint.QueryPort, 20));
        Assert.Null(probe.Receive(challenge, ADDRESS, _endpoint.QueryPort, 30));
        Assert.True(probe.IsComplete);
        Assert.Null(probe.Result);
    }

    [Theory]
    [InlineData(false, 7777)]
    [InlineData(true, 23456)]
    public void Probe_UsesAdvertisedGamePortOnlyForDiscovery(bool discovered, int expectedPort)
    {
        A2SProbe probe = new(_endpoint, ADDRESS, 0, discovered);
        probe.StartRequest();
        probe.Receive(ServerQueryProtocol.EncodeInfoResponse(ServerQueryTests.Sample() with { GamePort = 23456 }), ADDRESS, _endpoint.QueryPort, 10);
        probe.Receive(ServerQueryProtocol.EncodeRulesResponse(ServerQueryTests.Sample()), ADDRESS, _endpoint.QueryPort, 20);
        Assert.Equal(new ServerEndpoint(_endpoint.Address, expectedPort, _endpoint.QueryPort), probe.Result!.Value.Endpoint);
    }

    [Fact]
    public void Probe_PreservesForwardedGamePortEvenWhenInternalPortMatchesExternalQueryPort()
    {
        ServerEndpoint forwarded = new(_endpoint.Address, 17000, 7777);
        A2SProbe probe = new(forwarded, ADDRESS, 0);
        probe.StartRequest();
        probe.Receive(ServerQueryProtocol.EncodeInfoResponse(ServerQueryTests.Sample()), ADDRESS, forwarded.QueryPort, 10);
        probe.Receive(ServerQueryProtocol.EncodeRulesResponse(ServerQueryTests.Sample()), ADDRESS, forwarded.QueryPort, 20);
        Assert.Equal(forwarded, probe.Result!.Value.Endpoint);
    }

    [Theory]
    [InlineData("192.0.2.2", 45001, 10001)]
    [InlineData("192.0.2.3", 45000, 10001)]
    [InlineData("192.0.2.2", 45000, 40000)]
    public void Responder_RejectsReplayedChallengeOutsideEndpointOrLifetime(string address, int port, ulong time)
    {
        A2SResponder responder = new();
        byte[] packet = Assert.Single(responder.Respond(ServerQueryProtocol.EncodeInfoRequest(), "192.0.2.2", 45000, 10000, ServerQueryTests.Sample()));
        Assert.True(ServerQueryProtocol.TryDecodeChallenge(packet, out int token));
        byte[] reply = Assert.Single(responder.Respond(ServerQueryProtocol.EncodeInfoRequest(token), address, port, time, ServerQueryTests.Sample()));
        Assert.True(ServerQueryProtocol.TryDecodeChallenge(reply, out _));
    }

    [Fact]
    public void Responder_SecretIsLocalToOwnerAndChallengesAreRateLimited()
    {
        A2SResponder first = new(secret: new byte[32]);
        A2SResponder second = new(secret: Enumerable.Repeat((byte)1, 32).ToArray());
        byte[] request = ServerQueryProtocol.EncodeInfoRequest();
        byte[] challenge = Assert.Single(first.Respond(request, ADDRESS, 45000, 0, ServerQueryTests.Sample()));
        Assert.True(ServerQueryProtocol.TryDecodeChallenge(challenge, out int token));
        Assert.True(ServerQueryProtocol.TryDecodeChallenge(Assert.Single(second.Respond(
            ServerQueryProtocol.EncodeInfoRequest(token), ADDRESS, 45000, 0, ServerQueryTests.Sample())), out _));
        for (int i = 0; i < 7; i++)
        {
            Assert.Single(first.Respond(request, ADDRESS, 45000, 0, ServerQueryTests.Sample()));
        }
        Assert.Empty(first.Respond(request, ADDRESS, 45000, 0, ServerQueryTests.Sample()));
    }
}
