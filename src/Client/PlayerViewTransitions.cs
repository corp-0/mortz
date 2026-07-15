namespace Mortz.Client;

[Flags]
internal enum PlayerViewTransition
{
    None = 0,
    ParryRaised = 1 << 0,
    ShellReloadStarted = 1 << 1,
    ReloadStopped = 1 << 2,
    Dashed = 1 << 3,
    TookDamage = 1 << 4,
}

/// <summary>Edge detection for one-shot presentation effects. Keeping this
/// separate from scene mutation makes prediction reconciliation rules explicit
/// and testable.</summary>
internal static class PlayerViewTransitions
{
    private const int LOCAL_DASH_CORRECTION_SLACK = 5;

    public static PlayerViewTransition Between(
        in PlayerViewState previous,
        in PlayerViewState next,
        bool isLocal)
    {
        PlayerViewTransition transitions = PlayerViewTransition.None;
        if (previous.ParryTicks == 0 && next.ParryTicks > 0)
            transitions |= PlayerViewTransition.ParryRaised;
        bool wasReloading = previous.ReloadTicks > 0 && previous.RespawnTicks == 0;
        bool isReloading = next.ReloadTicks > 0 && next.RespawnTicks == 0;
        if (isReloading && (!wasReloading || next.Ammo > previous.Ammo))
            transitions |= PlayerViewTransition.ShellReloadStarted;
        if (wasReloading && !isReloading)
            transitions |= PlayerViewTransition.ReloadStopped;
        int dashRise = isLocal ? LOCAL_DASH_CORRECTION_SLACK : 1;
        if (next.DashCooldown - previous.DashCooldown >= dashRise)
            transitions |= PlayerViewTransition.Dashed;
        if (next.Health < previous.Health)
            transitions |= PlayerViewTransition.TookDamage;
        return transitions;
    }
}
