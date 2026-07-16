namespace Mortz.Server.Chat;

/// <summary>Verifier exposed to server features that own privileged actions.</summary>
public interface IServerAdminAuthorizer
{
    bool TryAuthorize(long peerId, ulong sequence, byte action,
        ReadOnlySpan<byte> payload, ReadOnlySpan<byte> tag);
}
