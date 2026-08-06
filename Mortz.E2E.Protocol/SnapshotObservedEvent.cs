namespace Mortz.E2E.Protocol;

public sealed record SnapshotObservedEvent(int Tick, E2EObservedPlayer[] Players) : E2EEvent;
