namespace Mortz.E2E.Protocol;

public sealed record DamagePlayerRequest(Guid Id, int PeerId, int Amount)
    : E2ERequest(Id), IE2ERequest<PlayerDamagedResponse>, IServerRequest;
