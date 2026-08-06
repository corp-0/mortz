namespace Mortz.E2E.Protocol;

public sealed record InputPlanAcceptedResponse(Guid Id, Guid PlanId) : E2EResponse(Id);
