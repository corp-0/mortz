namespace Mortz.E2E.Protocol;

public sealed record ClientStateRequest(Guid Id)
    : E2ERequest(Id), IE2ERequest<ClientStateResponse>, IClientRequest;
