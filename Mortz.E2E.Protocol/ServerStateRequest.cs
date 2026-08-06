namespace Mortz.E2E.Protocol;

public sealed record ServerStateRequest(Guid Id)
    : E2ERequest(Id), IE2ERequest<ServerStateResponse>, IServerRequest;
