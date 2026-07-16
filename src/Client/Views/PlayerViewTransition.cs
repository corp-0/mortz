namespace Mortz.Client.Views;

[Flags]
internal enum PlayerViewTransition
{
    NONE = 0,
    PARRY_RAISED = 1 << 0,
    SHELL_RELOAD_STARTED = 1 << 1,
    RELOAD_STOPPED = 1 << 2,
    DASHED = 1 << 3,
    TOOK_DAMAGE = 1 << 4,
}
