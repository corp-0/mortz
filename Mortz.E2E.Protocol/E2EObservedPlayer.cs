using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public readonly record struct E2EObservedPlayer(int PeerId, Vec2 Position, int Health);
