namespace Mortz.E2E.Protocol;

public sealed record ShutdownStartedResponse(Guid Id) : E2EResponse(Id);
