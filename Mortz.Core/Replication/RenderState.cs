using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

public readonly record struct RenderPlayer(int PeerId, Vec2 Position, byte Aim, byte Skin, RopeMode Rope, Vec2 RopePoint,
    byte Ammo, byte ReloadTicks, byte Health, ushort RespawnTicks, byte SpawnImmunityTicks,
    byte ParryTicks, byte DashCooldown);

public readonly record struct RenderMortar(ushort Id, int OwnerId, bool Deflected, int SpawnSeq,
    Vec2 Position, Vec2 Velocity);
