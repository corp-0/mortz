namespace Mortz.E2E.Protocol;

public sealed record PlayerLeftEvent(int PeerId, E2EPhase Phase) : E2EEvent;
