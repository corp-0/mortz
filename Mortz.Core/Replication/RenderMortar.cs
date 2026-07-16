using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

public readonly record struct RenderMortar(ushort Id, int OwnerId, bool Deflected, int SpawnSeq,
    Vec2 Position, Vec2 Velocity);
