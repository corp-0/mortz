using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>
/// Corner readout for the local player (exact health + the shell row) and a
/// short full-screen red flash whenever their health drops, so grazes register
/// even while watching the enemy. Health here is the last acked server value,
/// never the prediction; respawns raise it and therefore never flash.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float DAMAGE_FLASH_TIME = 0.35f; // s
    private const float DAMAGE_FLASH_ALPHA = 0.3f;

    [Export] private Label _healthLabel = null!;
    [Export] private ShellRow _shells = null!;
    [Export] private ColorRect _damageFlash = null!;

    private int _lastHealth = -1;
    private float _flash;

    public void UpdateFrom(in PlayerState local)
    {
        _healthLabel.Text = local.Health.ToString();
        _shells.SetAmmo(local.Ammo);

        if (_lastHealth >= 0 && local.Health < _lastHealth)
            _flash = DAMAGE_FLASH_TIME;
        _lastHealth = local.Health;
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0f)
            return;
        _flash = MathF.Max(0f, _flash - (float)delta);
        _damageFlash.Color = new Color(1, 0, 0, DAMAGE_FLASH_ALPHA * _flash / DAMAGE_FLASH_TIME);
    }
}
