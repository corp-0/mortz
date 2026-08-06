using Mortz.Core.Net;
using Mortz.Core.Net.Abuse;
using Mortz.Core.Net.Names;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class NetworkPolicyTests
{
    [Fact]
    public void Gate_AllowsHelloOnceAndCleansDisconnect()
    {
        var gate = new PeerGate(helloTimeoutMs: 100);
        gate.Connected(7, nowMs: 10);
        Assert.True(gate.TryValidate(7));
        Assert.True(gate.IsValidated(7));
        Assert.Equal([7], gate.ValidatedPeers.ToArray());
        Assert.False(gate.TryValidate(7));
        Assert.True(gate.Remove(7));
        Assert.False(gate.IsValidated(7));
        Assert.Empty(gate.ValidatedPeers);
        Assert.False(gate.TryValidate(7));
    }

    [Fact]
    public void Gate_ExpiresSilentPeersAndResetClearsAllState()
    {
        var gate = new PeerGate(helloTimeoutMs: 100);
        gate.Connected(1, nowMs: 50);
        gate.Connected(2, nowMs: 75);
        Assert.Empty(gate.Expire(149));
        Assert.Equal([1], gate.Expire(150));
        Assert.True(gate.TryValidate(2));

        gate.Connected(3, nowMs: 100);
        gate.Reset();
        Assert.Empty(gate.ValidatedPeers);
        Assert.Empty(gate.Expire(ulong.MaxValue));
        Assert.False(gate.TryValidate(3));
    }

    [Fact]
    public void Gate_NeverExpiresAValidatedPeer()
    {
        var gate = new PeerGate(helloTimeoutMs: 100);
        gate.Connected(4, nowMs: 0);
        Assert.True(gate.TryValidate(4));
        Assert.Empty(gate.Expire(ulong.MaxValue));
        Assert.True(gate.IsValidated(4));
    }

    [Fact]
    public void Gate_RemoveReportsFalseForAPeerThatNeverValidated()
    {
        var gate = new PeerGate(helloTimeoutMs: 100);
        gate.Connected(9, nowMs: 0);
        Assert.False(gate.Remove(9));
        Assert.False(gate.Remove(9));
    }

    [Fact]
    public void Bucket_AllowsBurstRejectsExhaustionAndRefills()
    {
        var bucket = new RateBucket(capacity: 3, tokensPerSecond: 2);
        Assert.True(bucket.Allow(1_000));
        Assert.True(bucket.Allow(1_000));
        Assert.True(bucket.Allow(1_000));
        Assert.False(bucket.Allow(1_000));
        Assert.False(bucket.Allow(1_499));
        Assert.True(bucket.Allow(1_500));
    }

    [Fact]
    public void Bucket_RejectsCostsItCanNeverAfford()
    {
        var bucket = new RateBucket(capacity: 3, tokensPerSecond: 2);
        Assert.False(bucket.Allow(0, cost: 4));
        Assert.False(bucket.Allow(0, cost: 0));
        Assert.True(bucket.Allow(0, cost: 3));
    }

    [Fact]
    public void Bucket_RejectsNonsenseRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateBucket(capacity: 0, tokensPerSecond: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateBucket(capacity: 1, tokensPerSecond: 0));
    }

    [Fact]
    public void Gate_BudgetsArePerPeerAndReconnectRestoresFreshBurst()
    {
        var gate = new PeerGate();
        gate.Connected(1, nowMs: 0);
        gate.Connected(2, nowMs: 0);

        Assert.True(gate.AllowMessage(1, 0, cost: 64));
        Assert.False(gate.AllowMessage(1, 0, cost: 1));
        Assert.True(gate.AllowMessage(2, 0, cost: 1));

        gate.Remove(1);
        Assert.False(gate.AllowMessage(1, 0, cost: 1));
        gate.Connected(1, nowMs: 0);
        Assert.True(gate.AllowMessage(1, 0, cost: 64));

        gate.Reset();
        Assert.False(gate.AllowMessage(2, 0, cost: 1));
    }

    [Fact]
    public void Gate_InputAndMessageBudgetsAreIndependent()
    {
        var gate = new PeerGate();
        gate.Connected(1, nowMs: 0);
        Assert.True(gate.AllowMessage(1, 0, cost: 64));
        Assert.False(gate.AllowMessage(1, 0, cost: 1));
        Assert.True(gate.AllowInput(1, 0));
    }

    [Fact]
    public void Gate_RejectsTrafficFromAPeerItNeverSaw()
    {
        var gate = new PeerGate();
        Assert.False(gate.AllowInput(99, 0));
        Assert.False(gate.AllowMessage(99, 0, cost: 1));
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
