namespace Mortz.E2E.Protocol;

public sealed record InputPlanStartedEvent(Guid PlanId, int FirstInputSequence) : E2EEvent;
