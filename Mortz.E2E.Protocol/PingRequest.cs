namespace Mortz.E2E.Protocol;

public sealed record PingRequest(Guid Id, string SchemaHash)
    : E2ERequest(Id), IE2ERequest<PongResponse>, IServerRequest, IClientRequest;
