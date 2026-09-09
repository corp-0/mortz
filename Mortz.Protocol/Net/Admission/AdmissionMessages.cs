namespace Mortz.Protocol.Net.Admission;

public enum AdmissionMode { GUEST, STEAM }

public enum AdmissionRejection
{
    INVALID_STATE,
    INCOMPATIBLE,
    INVALID_PROFILE,
    SERVER_FULL,
    HELLO_TIMEOUT,
    PROOF_TIMEOUT,
    VERIFICATION_TIMEOUT,
    INVALID_PROOF,
    DUPLICATE_ACCOUNT,
    VERIFICATION_FAILED,
    AUTHENTICATION_UNAVAILABLE,
    REVOKED,
    TICKET_FAILED,
    TICKET_TIMEOUT,
    ADMISSION_TIMEOUT,
    RECIPIENT_MISMATCH,
    SERVER_STOPPED,
}

public readonly record struct AdmissionHello(int ProtocolVersion, ulong SchemaHash, string Name, int Skin, bool Steam);
public readonly record struct AdmissionOffer(int Attempt, AdmissionMode Mode, ulong ServerAccountId);
public readonly record struct AdmissionAccepted(int Attempt, AdmissionMode Mode);
public readonly record struct AdmissionRejected(int Attempt, AdmissionRejection Reason);

// Keep proof bytes out of generated record diagnostics.
public class SteamProof(int attempt, ulong accountId, byte[]? ticket)
{
    public int Attempt { get; } = attempt;
    public ulong AccountId { get; } = accountId;
    public byte[] Ticket { get; } = ticket ?? [];
}

public static class AdmissionLimits
{
    public const ulong HELLO_MS = 5_000;
    public const ulong TICKET_MS = 10_000;
    public const ulong PROOF_MS = 15_000;
    public const ulong VERIFICATION_MS = 20_000;
    public const ulong CLIENT_MS = 40_000;
    public const int TICKET_BYTES = 1_024;
    public const int RESERVATIONS = NetConfig.MAX_PLAYERS;

    public static ulong Deadline(ulong now, ulong duration) =>
        ulong.MaxValue - now < duration ? ulong.MaxValue : now + duration;
}

public static class AdmissionReasons
{
    public static string Describe(AdmissionRejection reason) => reason switch
    {
        AdmissionRejection.INCOMPATIBLE => "Client and server versions are incompatible.",
        AdmissionRejection.INVALID_PROFILE => "Invalid player profile.",
        AdmissionRejection.SERVER_FULL => "Server is full.",
        AdmissionRejection.DUPLICATE_ACCOUNT => "This Steam account is already connected or finishing disconnecting.",
        AdmissionRejection.AUTHENTICATION_UNAVAILABLE => "Steam authentication became unavailable. Reconnect to try again.",
        AdmissionRejection.REVOKED => "Steam verification was revoked.",
        AdmissionRejection.TICKET_FAILED => "Steam could not create a ticket.",
        AdmissionRejection.RECIPIENT_MISMATCH => "Server Steam identity does not match the selected server.",
        AdmissionRejection.HELLO_TIMEOUT or AdmissionRejection.PROOF_TIMEOUT or
            AdmissionRejection.VERIFICATION_TIMEOUT or AdmissionRejection.TICKET_TIMEOUT or
            AdmissionRejection.ADMISSION_TIMEOUT => "Connection admission timed out.",
        AdmissionRejection.SERVER_STOPPED => "Server is shutting down.",
        _ => "Connection admission was rejected.",
    };
}
