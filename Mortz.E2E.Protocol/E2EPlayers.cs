using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public readonly record struct E2EObservedPlayer(int PeerId, Vec2 Position, int Health);

public readonly record struct E2EServerPlayer(
    int PeerId,
    string Name,
    Vec2 Position,
    Vec2 Velocity,
    int Health,
    int RespawnTicks);
