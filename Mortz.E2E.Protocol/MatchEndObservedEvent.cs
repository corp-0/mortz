namespace Mortz.E2E.Protocol;

public sealed record MatchEndObservedEvent(E2EVictorKind Kind, int VictorId) : E2EEvent;
