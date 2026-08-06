using System.Security.Cryptography;
using Mortz.Core.Net;
using Mortz.Core.Net.Abuse;

namespace Mortz.Core.Admin;

/// <summary>One player's admin state. Framework-disposed on disconnect, so key
/// material never waits for the GC.</summary>
public sealed class AdminSession : IDisposable
{
    public readonly byte[] Id = RandomNumberGenerator.GetBytes(AdminCrypto.SESSION_ID_BYTES);

    public byte[]? Challenge;
    public ulong ChallengeDeadlineMs;
    public byte[]? AdminKey;
    public ulong LastCommandSequence;
    public RateBucket Attempts = new(capacity: 3, tokensPerSecond: 0.05);

    public void ClearChallenge()
    {
        if (Challenge != null)
            CryptographicOperations.ZeroMemory(Challenge);
        Challenge = null;
        ChallengeDeadlineMs = 0;
    }

    public void ClearAdminKey()
    {
        if (AdminKey != null)
            CryptographicOperations.ZeroMemory(AdminKey);
        AdminKey = null;
        LastCommandSequence = 0;
    }

    public void Dispose()
    {
        ClearChallenge();
        ClearAdminKey();
        CryptographicOperations.ZeroMemory(Id);
    }
}
