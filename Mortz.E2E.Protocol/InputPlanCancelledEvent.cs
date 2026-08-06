namespace Mortz.E2E.Protocol;

public sealed record InputPlanCancelledEvent(Guid PlanId, int LastInputSequence) : E2EEvent;
