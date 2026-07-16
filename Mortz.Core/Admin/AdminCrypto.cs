using System.Security.Cryptography;
using System.Text;

namespace Mortz.Core.Admin;

public static class AdminCrypto
{
    public const int SESSION_ID_BYTES = 16;
    public const int NONCE_BYTES = 32;
    public const int CHALLENGE_BYTES = SESSION_ID_BYTES + NONCE_BYTES;
    public const int KEY_BYTES = 32;
    public const int TAG_BYTES = 32;

    private static readonly byte[] _proofContext = Encoding.UTF8.GetBytes("mortz-admin-proof-v1");
    private static readonly byte[] _sessionContext = Encoding.UTF8.GetBytes("mortz-admin-session-v1");
    private static readonly byte[] _commandContext = Encoding.UTF8.GetBytes("mortz-admin-command-v1");

    public static byte[] DerivePasswordKey(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static byte[] BuildChallenge(ReadOnlySpan<byte> sessionId, ReadOnlySpan<byte> nonce)
    {
        if (sessionId.Length != SESSION_ID_BYTES)
            throw new ArgumentException($"Session id must be {SESSION_ID_BYTES} bytes.", nameof(sessionId));
        if (nonce.Length != NONCE_BYTES)
            throw new ArgumentException($"Nonce must be {NONCE_BYTES} bytes.", nameof(nonce));
        byte[] challenge = new byte[CHALLENGE_BYTES];
        sessionId.CopyTo(challenge);
        nonce.CopyTo(challenge.AsSpan(SESSION_ID_BYTES));
        return challenge;
    }

    public static byte[] ComputeProof(ReadOnlySpan<byte> passwordKey, long peerId,
        ReadOnlySpan<byte> challenge) =>
        Compute(passwordKey, _proofContext, peerId, challenge);

    public static byte[] DeriveSessionKey(ReadOnlySpan<byte> passwordKey, long peerId,
        ReadOnlySpan<byte> challenge) =>
        Compute(passwordKey, _sessionContext, peerId, challenge);

    public static byte[] ComputeCommandTag(ReadOnlySpan<byte> sessionKey, long peerId,
        ulong sequence, byte action, ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(_commandContext);
        writer.Write(peerId);
        writer.Write(sequence);
        writer.Write(action);
        writer.Write(payload.Length);
        writer.Write(payload);
        return HMACSHA256.HashData(sessionKey, stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static byte[] Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> context,
        long peerId, ReadOnlySpan<byte> challenge)
    {
        if (key.Length != KEY_BYTES)
            throw new ArgumentException($"Key must be {KEY_BYTES} bytes.", nameof(key));
        if (challenge.Length != CHALLENGE_BYTES)
            throw new ArgumentException($"Challenge must be {CHALLENGE_BYTES} bytes.", nameof(challenge));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(context);
        writer.Write(peerId);
        writer.Write(challenge);
        return HMACSHA256.HashData(key, stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
