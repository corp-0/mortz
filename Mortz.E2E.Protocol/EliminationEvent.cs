namespace Mortz.E2E.Protocol;

public sealed record EliminationEvent(
    int Tick,
    int KillerId,
    int VictimId,
    bool Suicide) : E2EEvent;
