namespace Mortz.E2E.Protocol;

public sealed record PlayerPlacedResponse(Guid Id, int AppliedTick) : E2EResponse(Id);
