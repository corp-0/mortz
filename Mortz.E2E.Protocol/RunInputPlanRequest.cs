namespace Mortz.E2E.Protocol;

public sealed record RunInputPlanRequest(Guid Id, BotInputPlan Plan)
    : E2ERequest(Id), IE2ERequest<InputPlanAcceptedResponse>, IClientRequest;
