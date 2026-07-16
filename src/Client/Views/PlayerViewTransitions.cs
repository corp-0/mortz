namespace Mortz.Client.Views;

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
        PlayerViewTransition transitions = PlayerViewTransition.NONE;
        if (previous.ParryTicks == 0 && next.ParryTicks > 0)
            transitions |= PlayerViewTransition.PARRY_RAISED;
        bool wasReloading = previous.ReloadTicks > 0 && previous.RespawnTicks == 0;
        bool isReloading = next.ReloadTicks > 0 && next.RespawnTicks == 0;
        if (isReloading && (!wasReloading || next.Ammo > previous.Ammo))
            transitions |= PlayerViewTransition.SHELL_RELOAD_STARTED;
        if (wasReloading && !isReloading)
            transitions |= PlayerViewTransition.RELOAD_STOPPED;
        int dashRise = isLocal ? LOCAL_DASH_CORRECTION_SLACK : 1;
        if (next.DashCooldown - previous.DashCooldown >= dashRise)
            transitions |= PlayerViewTransition.DASHED;
        if (next.Health < previous.Health)
            transitions |= PlayerViewTransition.TOOK_DAMAGE;
        return transitions;
    }
}
