using Godot;
using Mortz.Core.Sim;

namespace Mortz.Client.Views;

/// <summary>Presentation and range policy for the per-shell reload progress.</summary>
public partial class PlayerReloadIndicator : ProgressBar
{
    private PlayerStats _stats = null!;

    public void Configure(PlayerStats stats) => _stats = stats;

    public void Apply(byte ammo, byte reloadTicks)
    {
        if (reloadTicks == 0)
        {
            Visible = false;
            return;
        }
        if (!Visible)
        {
            MinValue = ammo;
            MaxValue = _stats.MaxAmmo;
            Visible = true;
        }
        Value = ammo + 1.0 - (double)reloadTicks / _stats.ReloadTicks;
    }
}
