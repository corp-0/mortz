using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public sealed record PlacePlayerRequest(Guid Id, int PeerId, Vec2 Position)
    : E2ERequest(Id), IE2ERequest<PlayerPlacedResponse>, IServerRequest;
