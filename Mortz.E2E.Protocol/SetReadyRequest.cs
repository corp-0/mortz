namespace Mortz.E2E.Protocol;

public sealed record SetReadyRequest(Guid Id, bool Ready)
    : E2ERequest(Id), IE2ERequest<ReadySentResponse>, IClientRequest;
