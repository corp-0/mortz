namespace Mortz.E2E.Protocol;

public sealed record ReadySentResponse(Guid Id) : E2EResponse(Id);
