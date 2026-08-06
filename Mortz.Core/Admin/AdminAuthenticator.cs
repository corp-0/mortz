using System.Security.Cryptography;
using System.Text;
using Mortz.Core.Net;

namespace Mortz.Core.Admin;

/// <summary>Admin challenge, grant, and privileged-command verifier. Holds the
/// password and the crypto; the caller owns each player's <see cref="AdminSession"/>.</summary>
public sealed class AdminAuthenticator : IDisposable
{
    // Raw password, not a key: PBKDF2 is salted with the per-attempt challenge,
    // so we can only derive inside Verify.
    private readonly byte[]? _passwordUtf8;
    private readonly ulong _challengeTimeoutMs;

    public AdminAuthenticator(string password,
        ulong challengeTimeoutMs = NetConfig.ADMIN_CHALLENGE_TIMEOUT_MS)
    {
        if (challengeTimeoutMs == 0)
            throw new ArgumentOutOfRangeException(nameof(challengeTimeoutMs));
        _challengeTimeoutMs = challengeTimeoutMs;
        if (password.Length > 0)
            _passwordUtf8 = Encoding.UTF8.GetBytes(password);
    }

    public bool Enabled => _passwordUtf8 != null;

    public AdminChallengeResult Begin(AdminSession session, ulong nowMs, ReadOnlySpan<byte> nonce,
        out byte[] challenge)
    {
        challenge = [];
        if (!Enabled)
            return AdminChallengeResult.DISABLED;
        if (!session.Attempts.Allow(nowMs))
            return AdminChallengeResult.RATE_LIMITED;
        challenge = AdminCrypto.BuildChallenge(session.Id, nonce);
        session.ClearChallenge();
        // A new challenge drops the old grant, otherwise client and server can
        // disagree about which session key is live.
        session.ClearAdminKey();
        session.Challenge = challenge.ToArray();
        session.ChallengeDeadlineMs = SaturatingAdd(nowMs, _challengeTimeoutMs);
        return AdminChallengeResult.STARTED;
    }

    public AdminProofResult Verify(AdminSession session, int peerId, ulong nowMs,
        ReadOnlySpan<byte> proof)
    {
        if (!Enabled)
            return AdminProofResult.DISABLED;
        if (session.Challenge == null)
            return AdminProofResult.NO_CHALLENGE;

        byte[] challenge = session.Challenge;
        ulong deadline = session.ChallengeDeadlineMs;
        session.Challenge = null;
        session.ChallengeDeadlineMs = 0;
        try
        {
            if (nowMs > deadline)
                return AdminProofResult.EXPIRED;
            if (proof.Length != AdminCrypto.TAG_BYTES)
                return AdminProofResult.INVALID;
            byte[] passwordKey = AdminCrypto.DerivePasswordKey(_passwordUtf8!, challenge);
            try
            {
                byte[] expected = AdminCrypto.ComputeProof(passwordKey, peerId, challenge);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(expected, proof))
                        return AdminProofResult.INVALID;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expected);
                }

                session.ClearAdminKey();
                session.AdminKey = AdminCrypto.DeriveSessionKey(passwordKey, peerId, challenge);
                session.LastCommandSequence = 0;
                return AdminProofResult.ACCEPTED;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
        }
    }

    public bool IsAdmin(AdminSession session) => session.AdminKey != null;

    public bool VerifyCommand(AdminSession session, int peerId, ulong sequence, byte action,
        ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tag)
    {
        if (session.AdminKey == null || sequence != session.LastCommandSequence + 1 ||
            tag.Length != AdminCrypto.TAG_BYTES)
        {
            return false;
        }

        byte[] expected = AdminCrypto.ComputeCommandTag(session.AdminKey, peerId, sequence, action,
            payload);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, tag))
                return false;
            session.LastCommandSequence = sequence;
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public void Dispose()
    {
        if (_passwordUtf8 != null)
            CryptographicOperations.ZeroMemory(_passwordUtf8);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
