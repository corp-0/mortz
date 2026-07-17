using System.Security.Cryptography;
using Mortz.Core.Net;

namespace Mortz.Core.Admin;

/// <summary>Connection-bound admin challenge, grant, and privileged-command verifier.</summary>
public sealed class AdminAuthenticator : IDisposable
{
    private sealed class Session(byte[] id)
    {
        public readonly byte[] Id = id;
        public byte[]? Challenge;
        public ulong ChallengeDeadlineMs;
        public byte[]? AdminKey;
        public ulong LastCommandSequence;
    }

    private readonly byte[]? _passwordKey;
    private readonly ulong _challengeTimeoutMs;
    private readonly Dictionary<long, Session> _sessions = new();
    private readonly PeerRateLimiter _attemptLimiter = new(capacity: 3, tokensPerSecond: 0.05);

    public AdminAuthenticator(string password,
        ulong challengeTimeoutMs = NetConfig.ADMIN_CHALLENGE_TIMEOUT_MS)
    {
        if (challengeTimeoutMs == 0)
            throw new ArgumentOutOfRangeException(nameof(challengeTimeoutMs));
        _challengeTimeoutMs = challengeTimeoutMs;
        if (password.Length > 0)
            _passwordKey = AdminCrypto.DerivePasswordKey(password);
    }

    public bool Enabled => _passwordKey != null;

    public void Connected(long peerId, ReadOnlySpan<byte> sessionId)
    {
        if (sessionId.Length != AdminCrypto.SESSION_ID_BYTES)
            throw new ArgumentException($"Session id must be {AdminCrypto.SESSION_ID_BYTES} bytes.", nameof(sessionId));
        Remove(peerId);
        _sessions.Add(peerId, new Session(sessionId.ToArray()));
    }

    public AdminChallengeResult Begin(long peerId, ulong nowMs, ReadOnlySpan<byte> nonce,
        out byte[] challenge)
    {
        challenge = [];
        if (!Enabled)
            return AdminChallengeResult.DISABLED;
        if (!_sessions.TryGetValue(peerId, out Session? session))
            return AdminChallengeResult.UNKNOWN_PEER;
        if (!_attemptLimiter.Allow(peerId, nowMs))
            return AdminChallengeResult.RATE_LIMITED;
        challenge = AdminCrypto.BuildChallenge(session.Id, nonce);
        ClearChallenge(session);
        // A new challenge drops the old grant, otherwise client and server can
        // disagree about which session key is live.
        ClearAdminKey(session);
        session.Challenge = challenge.ToArray();
        session.ChallengeDeadlineMs = SaturatingAdd(nowMs, _challengeTimeoutMs);
        return AdminChallengeResult.STARTED;
    }

    public AdminProofResult Verify(long peerId, ulong nowMs, ReadOnlySpan<byte> proof)
    {
        if (!Enabled)
            return AdminProofResult.DISABLED;
        if (!_sessions.TryGetValue(peerId, out Session? session))
            return AdminProofResult.UNKNOWN_PEER;
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
            byte[] expected = AdminCrypto.ComputeProof(_passwordKey!, peerId, challenge);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expected, proof))
                    return AdminProofResult.INVALID;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
            }

            ClearAdminKey(session);
            session.AdminKey = AdminCrypto.DeriveSessionKey(_passwordKey!, peerId, challenge);
            session.LastCommandSequence = 0;
            return AdminProofResult.ACCEPTED;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
        }
    }

    public bool IsAdmin(long peerId) =>
        _sessions.TryGetValue(peerId, out Session? session) && session.AdminKey != null;

    public bool VerifyCommand(long peerId, ulong sequence, byte action,
        ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tag)
    {
        if (!_sessions.TryGetValue(peerId, out Session? session) || session.AdminKey == null ||
            sequence != session.LastCommandSequence + 1 || tag.Length != AdminCrypto.TAG_BYTES)
            return false;
        byte[] expected = AdminCrypto.ComputeCommandTag(session.AdminKey, peerId, sequence, action, payload);
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

    public void Remove(long peerId)
    {
        _attemptLimiter.Remove(peerId);
        if (!_sessions.Remove(peerId, out Session? session))
            return;
        ClearChallenge(session);
        ClearAdminKey(session);
        CryptographicOperations.ZeroMemory(session.Id);
    }

    public void Reset()
    {
        foreach (long peerId in _sessions.Keys.ToArray())
        {
            Remove(peerId);
        }
        _attemptLimiter.Reset();
    }

    public void Dispose()
    {
        Reset();
        if (_passwordKey != null)
            CryptographicOperations.ZeroMemory(_passwordKey);
    }

    private static void ClearChallenge(Session session)
    {
        if (session.Challenge != null)
            CryptographicOperations.ZeroMemory(session.Challenge);
        session.Challenge = null;
        session.ChallengeDeadlineMs = 0;
    }

    private static void ClearAdminKey(Session session)
    {
        if (session.AdminKey != null)
            CryptographicOperations.ZeroMemory(session.AdminKey);
        session.AdminKey = null;
        session.LastCommandSequence = 0;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
