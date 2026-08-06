namespace Mortz.E2E.Protocol;

public sealed record InputPlanCompletedEvent(Guid PlanId, int LastInputSequence) : E2EEvent;
