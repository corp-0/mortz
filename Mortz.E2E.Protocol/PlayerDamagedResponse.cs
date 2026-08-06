namespace Mortz.E2E.Protocol;

public sealed record PlayerDamagedResponse(
    Guid Id,
    int RemainingHealth,
    bool Died,
    int AppliedTick) : E2EResponse(Id);
