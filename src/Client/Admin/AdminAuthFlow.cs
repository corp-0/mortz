using System.Security.Cryptography;
using System.Text;
using Mortz.Core.Admin;
using Mortz.Core.Net.Admin;

namespace Mortz.Client.Admin;

/// <summary>Client half of the admin challenge-response handshake: owns the
/// derived secrets and the signing sequence, answers the wire messages its
/// owner forwards, and never lets a password or key linger in memory.</summary>
public sealed class AdminAuthFlow
{
    // Raw password, not a key: PBKDF2 is salted with the server challenge,
    // so derivation waits for it.
    private byte[]? _pendingPassword;
    private byte[]? _pendingAdminKey;
    private byte[]? _adminKey;
    private ulong _nextSequence = 1;

    public bool IsAdmin => _adminKey != null;

    /// <summary>Starts over with a fresh password: any prior authority is
    /// dropped before the request goes out.</summary>
    public void Begin(string password)
    {
        Reset();
        _pendingPassword = Encoding.UTF8.GetBytes(password);
        new AdminAuthRequestMsg().SendToServer();
    }

    /// <summary>Answers the server challenge with a proof; false when no
    /// authentication is pending or the challenge is malformed (pending
    /// secrets are dropped either way).</summary>
    public bool TryAnswerChallenge(int localPeerId, AdminChallengeMsg message)
    {
        if (_pendingPassword == null || localPeerId == 0 ||
            message.Challenge.Length != AdminCrypto.CHALLENGE_BYTES)
        {
            ClearPending();
            return false;
        }
        byte[] passwordKey = AdminCrypto.DerivePasswordKey(_pendingPassword, message.Challenge);
        CryptographicOperations.ZeroMemory(_pendingPassword);
        _pendingPassword = null;
        byte[] proof = AdminCrypto.ComputeProof(passwordKey, localPeerId, message.Challenge);
        _pendingAdminKey = AdminCrypto.DeriveSessionKey(passwordKey, localPeerId,
            message.Challenge);
        CryptographicOperations.ZeroMemory(passwordKey);
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
    public bool TrySign(int localPeerId, byte action, ReadOnlySpan<byte> payload,
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
        if (_pendingPassword != null)
            CryptographicOperations.ZeroMemory(_pendingPassword);
        if (_pendingAdminKey != null)
            CryptographicOperations.ZeroMemory(_pendingAdminKey);
        _pendingPassword = null;
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
