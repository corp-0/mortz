using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

public readonly record struct RenderPlayer(int PeerId, Vec2 Position, byte Aim, byte Skin, RopeMode Rope, Vec2 RopePoint,
    byte Ammo, byte ReloadTicks, byte Health, byte RespawnTicks, byte SpawnImmunityTicks,
    byte ParryTicks, byte DashCooldown);
