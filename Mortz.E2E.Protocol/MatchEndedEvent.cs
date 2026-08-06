namespace Mortz.E2E.Protocol;

public sealed record MatchEndedEvent(int Tick, E2EVictorKind Kind, int VictorId) : E2EEvent;
