using Mortz.Core.Match;

namespace Mortz.Core.Sim;

/// <summary>
/// The mortar magazine, as a pure function so the server and prediction run
/// the exact same rules. No cooldown between shots. A reload (auto when
/// empty, R anytime below full) banks one shell per reload interval; firing
/// scraps the one in progress and stops the reload. Spawning the shell is the
/// caller's job.
/// </summary>
public static class WeaponSim
{
    /// <summary>Press edges come from the pre-tick buttons; PlayerSim has
    /// already overwritten PrevButtons with this tick's by the time this runs.</summary>
    /// <returns>true when a shell fires this tick.</returns>
    public static bool Tick(ref PlayerState p, PlayerInput input, InputButtons prevButtons,
        PlayerStats stats, int inputSeq)
    {
        if (p.RespawnTicks > 0)
            return false; // corpses don't fire or reload

        bool firePressed = input.Fire && !prevButtons.HasFlag(InputButtons.FIRE);
        bool reloadPressed = input.Reload && !prevButtons.HasFlag(InputButtons.RELOAD);

        bool fired = firePressed && p.Ammo > 0 && CombatEligibility.CanFire(p, inputSeq);
        if (fired)
        {
            p.Ammo--;
            p.ReloadTicks = 0; // shooting scraps the shell being loaded
        }

        if (p.ReloadTicks == 0 && (p.Ammo == 0 || (reloadPressed && p.Ammo < stats.MaxAmmo)))
            p.ReloadTicks = stats.ReloadPerShellTicks;
        if (p.ReloadTicks > 0 && --p.ReloadTicks == 0 && ++p.Ammo < stats.MaxAmmo)
            p.ReloadTicks = stats.ReloadPerShellTicks; // next shell

        return fired;
    }

    /// <summary>The one spawn formula, shared so a predicted shell and the
    /// authoritative one fly the same path.</summary>
    public static MortarState NewShell(ushort id, int spawnSeq, in PlayerState shooter, PlayerInput input,
        MatchConfig cfg)
    {
        Vec2 center = shooter.Position with { Y = shooter.Position.Y - SimConfig.PLAYER_HALF_HEIGHT };
        return new MortarState
        {
            Id = id,
            OwnerId = shooter.PeerId,
            FiredBy = shooter.PeerId,
            SpawnSeq = spawnSeq,
            Position = center + input.AimDir * SimConfig.MORTAR_MUZZLE_OFFSET,
            Velocity = input.AimDir * cfg.MortarSpeed + shooter.Velocity * cfg.MortarInherit,
        };
    }
}
