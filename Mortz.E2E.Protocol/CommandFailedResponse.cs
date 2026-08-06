namespace Mortz.E2E.Protocol;

public sealed record CommandFailedResponse(
    Guid Id,
    E2EError Error,
    string Message) : E2EResponse(Id);
