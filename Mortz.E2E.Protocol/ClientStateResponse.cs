using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public sealed record ClientStateResponse(
    Guid Id,
    int LastSnapshotTick,
    Vec2 PredictedPosition,
    E2EObservedPlayer[] Players) : E2EResponse(Id);
