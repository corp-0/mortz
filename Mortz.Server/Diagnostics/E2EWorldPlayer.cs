using Mortz.Core.Sim;

namespace Mortz.Server.Diagnostics;

public readonly record struct E2EWorldPlayer(
    int PeerId,
    Vec2 Position,
    Vec2 Velocity,
    int Health,
    int RespawnTicks);
