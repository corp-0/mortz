using System.Security.Cryptography;
using System.Text;
using Mortz.Core.Admin;
using Xunit;

namespace Mortz.Tests.Core.Admin;

public class AdminAuthenticatorTests
{
    private const string PASSWORD = "correct horse battery staple with entropy";
    private const int PEER = 77;

    private readonly AdminSession _session = new();

    [Fact]
    public void Challenge_AuthenticatesConnectionAndSignedCommandsInOrder()
    {
        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 10_000);
        byte[] challenge = Begin(auth, _session, nowMs: 1_000, Nonce(2));
        byte[] passwordKey = Key(challenge);
        byte[] proof = AdminCrypto.ComputeProof(passwordKey, PEER, challenge);
        byte[] sessionKey = AdminCrypto.DeriveSessionKey(passwordKey, PEER, challenge);

        Assert.Equal(AdminProofResult.ACCEPTED, auth.Verify(_session, PEER, 1_001, proof));
        Assert.True(auth.IsAdmin(_session));

        byte[] payload = [1, 2, 3];
        byte[] tag1 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 1, 4, payload);
        Assert.True(auth.VerifyCommand(_session, PEER, 1, 4, payload, tag1));
        Assert.False(auth.VerifyCommand(_session, PEER, 1, 4, payload, tag1));
        byte[] tag3 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 3, 4, payload);
        Assert.False(auth.VerifyCommand(_session, PEER, 3, 4, payload, tag3));
        byte[] tag2 = AdminCrypto.ComputeCommandTag(sessionKey, PEER, 2, 4, payload);
        tag2[0] ^= 0x80;
        Assert.False(auth.VerifyCommand(_session, PEER, 2, 4, payload, tag2));

        CryptographicOperations.ZeroMemory(passwordKey);
        CryptographicOperations.ZeroMemory(sessionKey);
    }

    [Fact]
    public void Challenge_IsOneShotAndExpires()
    {
        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 100);
        byte[] challenge = Begin(auth, _session, nowMs: 50, Nonce(3));
        byte[] proof = AdminCrypto.ComputeProof(Key(challenge), PEER, challenge);

        Assert.Equal(AdminProofResult.EXPIRED, auth.Verify(_session, PEER, 151, proof));
        Assert.Equal(AdminProofResult.NO_CHALLENGE, auth.Verify(_session, PEER, 151, proof));

        challenge = Begin(auth, _session, nowMs: 200, Nonce(4));
        proof = AdminCrypto.ComputeProof(Key(challenge), PEER, challenge);
        proof[0] ^= 1;
        Assert.Equal(AdminProofResult.INVALID, auth.Verify(_session, PEER, 201, proof));
        Assert.Equal(AdminProofResult.NO_CHALLENGE, auth.Verify(_session, PEER, 201, proof));
    }

    [Fact]
    public void ReconnectingPeerGetsNewServerSessionAndNoOldGrant()
    {
        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 10_000);
        byte[] first = Begin(auth, _session, 1, Nonce(5));
        byte[] key = Key(first);
        Assert.Equal(AdminProofResult.ACCEPTED,
            auth.Verify(_session, PEER, 2, AdminCrypto.ComputeProof(key, PEER, first)));

        // A reconnect is a fresh AdminSession on the same peer id.
        using AdminSession reconnected = new();
        Assert.False(auth.IsAdmin(reconnected));
        Assert.Equal(AdminProofResult.NO_CHALLENGE,
            auth.Verify(reconnected, PEER, 3, AdminCrypto.ComputeProof(key, PEER, first)));
        byte[] second = Begin(auth, reconnected, 4, Nonce(6));
        Assert.NotEqual(first, second);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void StartingNewChallengeReplacesExistingGrant()
    {
        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 10_000);
        byte[] first = Begin(auth, _session, 1, Nonce(7));
        byte[] key = Key(first);
        Assert.Equal(AdminProofResult.ACCEPTED,
            auth.Verify(_session, PEER, 2, AdminCrypto.ComputeProof(key, PEER, first)));
        Assert.True(auth.IsAdmin(_session));

        Begin(auth, _session, 3, Nonce(8));

        Assert.False(auth.IsAdmin(_session));
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void DisabledAndRateLimitedRequestsFailClosed()
    {
        using AdminAuthenticator disabled = new("");
        Assert.Equal(AdminChallengeResult.DISABLED,
            disabled.Begin(_session, 0, Nonce(1), out _));

        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 10_000);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(AdminChallengeResult.STARTED,
                auth.Begin(_session, 0, Nonce((byte)(10 + i)), out _));
        }
        Assert.Equal(AdminChallengeResult.RATE_LIMITED,
            auth.Begin(_session, 0, Nonce(20), out _));
    }

    [Fact]
    public void DisposingTheSessionZeroesItsKeyMaterial()
    {
        using AdminAuthenticator auth = new(PASSWORD, challengeTimeoutMs: 10_000);
        byte[] challenge = Begin(auth, _session, 1, Nonce(9));
        byte[] key = Key(challenge);
        Assert.Equal(AdminProofResult.ACCEPTED,
            auth.Verify(_session, PEER, 2, AdminCrypto.ComputeProof(key, PEER, challenge)));

        _session.Dispose();

        Assert.Null(_session.AdminKey);
        Assert.False(auth.IsAdmin(_session));
        Assert.All(_session.Id, b => Assert.Equal(0, b));
        CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] Begin(AdminAuthenticator auth, AdminSession session, ulong nowMs,
        byte[] nonce)
    {
        Assert.Equal(AdminChallengeResult.STARTED,
            auth.Begin(session, nowMs, nonce, out byte[] challenge));
        return challenge;
    }

    private static byte[] Key(byte[] challenge) =>
        AdminCrypto.DerivePasswordKey(Encoding.UTF8.GetBytes(PASSWORD), challenge);

    private static byte[] Nonce(byte value) =>
        Enumerable.Repeat(value, AdminCrypto.NONCE_BYTES).ToArray();
}
