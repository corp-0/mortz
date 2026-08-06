namespace Mortz.E2E.Protocol;

public sealed record PhaseChangedEvent(E2EPhase Phase) : E2EEvent;
