using Mortz.Core;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core;

public class NetworkPolicyTests
{
    [Fact]
    public void Admission_AllowsHelloOnceAndCleansDisconnect()
    {
        var state = new PeerAdmissionState(timeoutMs: 100);
        state.Connected(7, nowMs: 10);
        Assert.True(state.TryValidate(7));
        Assert.True(state.IsValidated(7));
        Assert.False(state.TryValidate(7));
        Assert.True(state.Remove(7));
        Assert.False(state.IsValidated(7));
        Assert.False(state.TryValidate(7));
    }

    [Fact]
    public void Admission_ExpiresSilentPeersAndResetClearsAllState()
    {
        var state = new PeerAdmissionState(timeoutMs: 100);
        state.Connected(1, nowMs: 50);
        state.Connected(2, nowMs: 75);
        Assert.Empty(state.Expire(149));
        Assert.Equal([1], state.Expire(150));
        Assert.True(state.TryValidate(2));

        state.Connected(3, nowMs: 100);
        state.Reset();
        Assert.Empty(state.ValidatedPeers);
        Assert.Empty(state.Expire(ulong.MaxValue));
        Assert.False(state.TryValidate(3));
    }

    [Fact]
    public void Limiter_AllowsBurstRejectsExhaustionAndRefills()
    {
        var limiter = new PeerRateLimiter(capacity: 3, tokensPerSecond: 2);
        Assert.True(limiter.Allow(1, 1_000));
        Assert.True(limiter.Allow(1, 1_000));
        Assert.True(limiter.Allow(1, 1_000));
        Assert.False(limiter.Allow(1, 1_000));
        Assert.False(limiter.Allow(1, 1_499));
        Assert.True(limiter.Allow(1, 1_500));
    }

    [Fact]
    public void Limiter_IsPerPeerAndCleanupRestoresFreshBurst()
    {
        var limiter = new PeerRateLimiter(capacity: 1, tokensPerSecond: 1);
        Assert.True(limiter.Allow(1, 0));
        Assert.False(limiter.Allow(1, 0));
        Assert.True(limiter.Allow(2, 0));
        limiter.Remove(1);
        Assert.True(limiter.Allow(1, 0));
        limiter.Reset();
        Assert.True(limiter.Allow(2, 0));
    }

    [Fact]
    public void EnvelopeCost_IsByteWeighted()
    {
        Assert.Equal(1, NetAbusePolicy.EnvelopeCost(0));
        Assert.Equal(2, NetAbusePolicy.EnvelopeCost(4096));
        Assert.True(NetAbusePolicy.EnvelopeCost(NetConfig.MAX_ENVELOPE_BYTES) > 16);
    }

    [Fact]
    public void PlayerNames_RemoveControlAndFormatCharactersWithoutSplittingRunes()
    {
        string sanitized = PlayerNameSanitizer.Sanitize("  Al\r\nice\u202E🙂  ");
        Assert.Equal("Alice🙂", sanitized);
        Assert.True(sanitized.Length <= NetConfig.MAX_NAME_LENGTH);

        string emoji = string.Concat(Enumerable.Repeat("🙂", 20));
        string capped = PlayerNameSanitizer.Sanitize(emoji);
        Assert.True(capped.Length <= NetConfig.MAX_NAME_LENGTH);
        Assert.DoesNotContain('\uFFFD', capped);
    }
}
