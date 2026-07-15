namespace Mortz.Core;

/// <summary>Who is allowed to join in right now. Callers still work out the
/// shot or the damage themselves; this only answers yes or no.</summary>
public static class CombatEligibility
{
    public static bool CanFire(in PlayerState player, int inputSeq) =>
        player.RespawnTicks == 0 &&
        player.SpawnImmunityTicks == 0 &&
        inputSeq > player.SpawnImmunityFireThroughSeq;

    public static bool CanTakeDamage(in PlayerState player) =>
        player.RespawnTicks == 0 && player.SpawnImmunityTicks == 0;
}
