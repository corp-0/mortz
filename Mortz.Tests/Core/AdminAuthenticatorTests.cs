using System.Security.Cryptography;
using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class AdminAuthenticatorTests
{
    private const string PASSWORD = "correct horse battery staple with entropy";
    private const long PEER = 77;

    [Fact]
    public void Challenge_AuthenticatesConnectionAndSignedCommandsInOrder()
    {
        using AdminAuthenticator auth = Connected();
        byte[] challenge = Begin(auth, nowMs: 1_000, Nonce(2));
        byte[] passwordKey = AdminCrypto.DerivePasswordKey(PASSWORD);
        byte[] proof = AdminCrypto.ComputeProof(passwordKey, PEER, challenge);
        byte[] sessionKey = AdminCrypto.DeriveSessionKey(passwordKey, PEER, challenge);

        Assert.Equal(AdminProofResult.ACCEPTED, auth.Verify(PEER, 1_001, proof));
        Assert.True(auth.IsAdmin(PEER));

        byte[] payload = [1, 2, 3];
        byte[] tag1 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 1, 4, payload);
        Assert.True(auth.VerifyCommand(PEER, 1, 4, payload, tag1));
        Assert.False(auth.VerifyCommand(PEER, 1, 4, payload, tag1));
        byte[] tag3 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 3, 4, payload);
        Assert.False(auth.VerifyCommand(PEER, 3, 4, payload, tag3));
        byte[] tag2 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 2, 4, payload);
        tag2[0] ^= 0x80;
        Assert.False(auth.VerifyCommand(PEER, 2, 4, payload, tag2));

        CryptographicOperations.ZeroMemory(passwordKey);
        CryptographicOperations.ZeroMemory(sessionKey);
    }

    [Fact]
    public void Challenge_IsOneShotAndExpires()
    {
        using AdminAuthenticator auth = Connected(challengeTimeoutMs: 100);
        byte[] challenge = Begin(auth, nowMs: 50, Nonce(3));
        byte[] key = AdminCrypto.DerivePasswordKey(PASSWORD);
        byte[] proof = AdminCrypto.ComputeProof(key, PEER, challenge);

        Assert.Equal(AdminProofResult.EXPIRED, auth.Verify(PEER, 151, proof));
        Assert.Equal(AdminProofResult.NO_CHALLENGE, auth.Verify(PEER, 151, proof));

        challenge = Begin(auth, nowMs: 200, Nonce(4));
        proof = AdminCrypto.ComputeProof(key, PEER, challenge);
        proof[0] ^= 1;
        Assert.Equal(AdminProofResult.INVALID, auth.Verify(PEER, 201, proof));
        Assert.Equal(AdminProofResult.NO_CHALLENGE, auth.Verify(PEER, 201, proof));
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void ReusedPeerIdGetsNewServerSessionAndNoOldGrant()
    {
        using AdminAuthenticator auth = Connected();
        byte[] first = Begin(auth, 1, Nonce(5));
        byte[] key = AdminCrypto.DerivePasswordKey(PASSWORD);
        Assert.Equal(AdminProofResult.ACCEPTED,
            auth.Verify(PEER, 2, AdminCrypto.ComputeProof(key, PEER, first)));

        auth.Connected(PEER, SessionId(9));
        Assert.False(auth.IsAdmin(PEER));
        Assert.Equal(AdminProofResult.NO_CHALLENGE,
            auth.Verify(PEER, 3, AdminCrypto.ComputeProof(key, PEER, first)));
        byte[] second = Begin(auth, 4, Nonce(6));
        Assert.NotEqual(first, second);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void StartingNewChallengeReplacesExistingGrant()
    {
        using AdminAuthenticator auth = Connected();
        byte[] first = Begin(auth, 1, Nonce(7));
        byte[] key = AdminCrypto.DerivePasswordKey(PASSWORD);
        Assert.Equal(AdminProofResult.ACCEPTED,
            auth.Verify(PEER, 2, AdminCrypto.ComputeProof(key, PEER, first)));
        Assert.True(auth.IsAdmin(PEER));

        Begin(auth, 3, Nonce(8));

        Assert.False(auth.IsAdmin(PEER));
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void DisabledUnknownAndRateLimitedRequestsFailClosed()
    {
        using AdminAuthenticator disabled = new("");
        disabled.Connected(PEER, SessionId(1));
        Assert.Equal(AdminChallengeResult.DISABLED,
            disabled.Begin(PEER, 0, Nonce(1), out _));

        using AdminAuthenticator auth = Connected();
        Assert.Equal(AdminChallengeResult.UNKNOWN_PEER,
            auth.Begin(999, 0, Nonce(1), out _));
        for (int i = 0; i < 3; i++)
            Assert.Equal(AdminChallengeResult.STARTED,
                auth.Begin(PEER, 0, Nonce((byte)(10 + i)), out _));
        Assert.Equal(AdminChallengeResult.RATE_LIMITED,
            auth.Begin(PEER, 0, Nonce(20), out _));
        auth.Remove(PEER);
        Assert.False(auth.IsAdmin(PEER));
    }

    private static AdminAuthenticator Connected(ulong challengeTimeoutMs = 10_000)
    {
        AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs);
        auth.Connected(PEER, SessionId(1));
        return auth;
    }

    private static byte[] Begin(AdminAuthenticator auth, ulong nowMs, byte[] nonce)
    {
        Assert.Equal(AdminChallengeResult.STARTED,
            auth.Begin(PEER, nowMs, nonce, out byte[] challenge));
        return challenge;
    }

    private static byte[] SessionId(byte value) =>
        Enumerable.Repeat(value, AdminCrypto.SESSION_ID_BYTES).ToArray();

    private static byte[] Nonce(byte value) =>
        Enumerable.Repeat(value, AdminCrypto.NONCE_BYTES).ToArray();
}
