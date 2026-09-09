using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class ServerRuntimeTests
{
    private static readonly ServerInfo _info = new("Test", "deathmatch", "duel", 0, 8, true, true, 7777, 45, 1);

    [Fact]
    public void InitializationFailureReleasesSdkBeforeFallbackBinds()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls) { InitializeResult = false };
        using ServerRuntime runtime = new(platform, _ => { calls.Add("bind"); return true; }, () => calls.Add("close"));
        runtime.Start(_info, 7778, true);
        Assert.Equal(["initialize", "dispose", "bind"], calls);
        Assert.Equal(QueryOwner.MORTZ, runtime.Capabilities.Query);
        Assert.True(runtime.Capabilities.GameListener);
        Assert.False(runtime.Capabilities.Authentication);
    }

    [Fact]
    public void PublicLoginFailureDoesNotTransferSdkQuerySocket()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls);
        using ServerRuntime runtime = new(platform, _ => { calls.Add("bind"); return true; }, () => calls.Add("close"));
        runtime.Start(_info, 7778, true);
        runtime.Advance(_info);
        Assert.Equal(QueryOwner.STEAM, runtime.Capabilities.Query);
        Assert.True(runtime.Capabilities.GameListener);
        Assert.False(runtime.Capabilities.Authentication);
        Assert.DoesNotContain("bind", calls);
        platform.AuthenticationAvailable = true;
        runtime.Advance(_info);
        Assert.True(runtime.Capabilities.Authentication);
        platform.AuthenticationAvailable = false;
        runtime.Advance(_info);
        Assert.False(runtime.Capabilities.Authentication);
        Assert.Equal(QueryOwner.STEAM, runtime.Capabilities.Query);
    }

    [Fact]
    public void PrivateQueryBindFailureStopsSteamButLeavesGameReady()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls) { AuthenticationAvailable = true };
        using ServerRuntime runtime = new(platform, _ => false, () => calls.Add("close"));
        runtime.Start(_info, 7778, false);
        Assert.True(runtime.Capabilities.GameListener);
        Assert.False(runtime.Capabilities.Authentication);
        Assert.Equal(QueryOwner.NONE, runtime.Capabilities.Query);
        Assert.Equal(["initialize", "dispose"], calls);
    }

    [Fact]
    public void ShutdownStopsSteamBeforeQueryAndIsIdempotent()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls);
        ServerRuntime runtime = new(platform, _ => true, () => calls.Add("close"));
        runtime.Start(_info, 7778, false);
        runtime.Dispose();
        runtime.Dispose();
        Assert.Equal(["initialize", "dispose", "close"], calls);
        Assert.False(runtime.Capabilities.GameListener);
    }

    [Fact]
    public void StandaloneDoesNotNeedPlatformAndReportsBindFailureSeparately()
    {
        using ServerRuntime runtime = new(null, _ => false, () => { });
        runtime.Start(_info, 7778, false);
        Assert.True(runtime.Capabilities.GameListener);
        Assert.Equal(QueryOwner.NONE, runtime.Capabilities.Query);
        Assert.False(runtime.Capabilities.Authentication);
    }

    private class FakePlatform(List<string> calls) : IServerPlatform
    {
        public bool InitializeResult { get; init; } = true;
        public bool AuthenticationAvailable { get; set; }
        public bool PublicationActive => false;
        public string Status => "Login pending";
        public bool Initialize(ServerInfo info, int queryPort, bool isPublic)
        {
            calls.Add("initialize");
            return InitializeResult;
        }
        public void Advance(ServerInfo info) { }
        public void Dispose() => calls.Add("dispose");
    }
}
