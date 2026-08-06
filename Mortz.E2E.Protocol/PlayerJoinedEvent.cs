namespace Mortz.E2E.Protocol;

public sealed record PlayerJoinedEvent(int PeerId, string Name, E2EPhase Phase) : E2EEvent;
