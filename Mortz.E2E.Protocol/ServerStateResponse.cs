namespace Mortz.E2E.Protocol;

public sealed record ServerStateResponse(
    Guid Id,
    E2EPhase Phase,
    int Tick,
    E2EServerPlayer[] Players) : E2EResponse(Id);
