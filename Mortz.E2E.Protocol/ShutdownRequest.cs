namespace Mortz.E2E.Protocol;

public sealed record ShutdownRequest(Guid Id)
    : E2ERequest(Id), IE2ERequest<ShutdownStartedResponse>, IServerRequest, IClientRequest;
