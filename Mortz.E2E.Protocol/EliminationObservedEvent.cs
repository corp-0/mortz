namespace Mortz.E2E.Protocol;

public sealed record EliminationObservedEvent(
    int LastSnapshotTick,
    int KillerId,
    int VictimId,
    bool Suicide) : E2EEvent;
