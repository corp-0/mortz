using Godot;

namespace Mortz.Client.Views;

/// <summary>Everything needed to present one player for one rendered frame.</summary>
public readonly record struct PlayerViewState(
    Vector2 Feet,
    byte Aim,
    byte Skin,
    byte Ammo,
    byte ReloadTicks,
    byte Health,
    byte RespawnTicks,
    byte ParryTicks,
    byte DashCooldown,
    byte SpawnImmunityTicks);
