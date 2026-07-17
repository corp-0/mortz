using System.Security.Cryptography;
using Mortz.Core.Admin;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Admin;

/// <summary>Client half of the admin challenge-response handshake: owns the
/// derived secrets and the signing sequence, answers the wire messages its
/// owner forwards, and never lets a password or key linger in memory.</summary>
public sealed class AdminAuthFlow
{
    private byte[]? _pendingPasswordKey;
    private byte[]? _pendingAdminKey;
    private byte[]? _adminKey;
    private ulong _nextSequence = 1;

    public bool IsAdmin => _adminKey != null;

    /// <summary>Starts over with a fresh password: any prior authority is
    /// dropped before the request goes out.</summary>
    public void Begin(string password)
    {
        Reset();
        _pendingPasswordKey = AdminCrypto.DerivePasswordKey(password);
        new AdminAuthRequestMsg().SendToServer();
    }

    /// <summary>Answers the server challenge with a proof; false when no
    /// authentication is pending or the challenge is malformed (pending
    /// secrets are dropped either way).</summary>
    public bool TryAnswerChallenge(long localPeerId, AdminChallengeMsg message)
    {
        if (_pendingPasswordKey == null || localPeerId == 0 ||
            message.Challenge.Length != AdminCrypto.CHALLENGE_BYTES)
        {
            ClearPending();
            return false;
        }
        byte[] proof = AdminCrypto.ComputeProof(_pendingPasswordKey, localPeerId,
            message.Challenge);
        _pendingAdminKey = AdminCrypto.DeriveSessionKey(_pendingPasswordKey, localPeerId,
            message.Challenge);
        CryptographicOperations.ZeroMemory(_pendingPasswordKey);
        _pendingPasswordKey = null;
        new AdminProofMsg(proof).SendToServer();
        CryptographicOperations.ZeroMemory(proof);
        return true;
    }

    /// <summary>The server's verdict: promotes the pending key or revokes the
    /// current one.</summary>
    public void ApplyState(AdminStateMsg message)
    {
        if (message.IsAdmin && _pendingAdminKey != null)
        {
            ClearAdminKey();
            _adminKey = _pendingAdminKey;
            _pendingAdminKey = null;
            _nextSequence = 1;
        }
        else if (!message.IsAdmin)
        {
            ClearAdminKey();
        }
        ClearPending();
    }

    /// <summary>Creates the proof for a future privileged mutation. The caller
    /// serializes this sequence, action, payload, and tag into its action message.</summary>
    public bool TrySign(long localPeerId, byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag)
    {
        sequence = 0;
        tag = [];
        if (_adminKey == null || localPeerId == 0)
            return false;
        sequence = _nextSequence++;
        tag = AdminCrypto.ComputeCommandTag(_adminKey, localPeerId, sequence, action, payload);
        return true;
    }

    public void Reset()
    {
        ClearPending();
        ClearAdminKey();
    }

    private void ClearPending()
    {
        if (_pendingPasswordKey != null)
            CryptographicOperations.ZeroMemory(_pendingPasswordKey);
        if (_pendingAdminKey != null)
            CryptographicOperations.ZeroMemory(_pendingAdminKey);
        _pendingPasswordKey = null;
        _pendingAdminKey = null;
    }

    private void ClearAdminKey()
    {
        if (_adminKey != null)
            CryptographicOperations.ZeroMemory(_adminKey);
        _adminKey = null;
        _nextSequence = 1;
    }
}
