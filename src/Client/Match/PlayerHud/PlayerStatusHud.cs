using Godot;
using Mortz.Core.Sim;

namespace Mortz.Client.Match.PlayerHud;

[GlobalClass]
public partial class PlayerStatusHud : Control
{
    private const float DAMAGE_FLASH_TIME = 0.35f; // s
    private const float DAMAGE_FLASH_ALPHA = 0.3f;

    [Export] private HealthBar _healthBar = null!;
    [Export] private ShellCounter _shells = null!;
    [Export] private AbilityIcon _parry = null!;
    [Export] private AbilityIcon _rope = null!;
    [Export] private ColorRect _damageFlash = null!;

    private int _lastHealth = -1;
    private float _flash;

    /// <summary>Must be called before entering the tree (GameView.Initialize does).</summary>
    public void Configure(PlayerStats stats)
    {
        _shells.Configure(stats.MaxAmmo);
        _healthBar.Configure(stats.MaxHealth);
    }

    public void UpdateFrom(in PlayerState local)
    {
        _healthBar.UpdateHealthBar(local.Health);
        _shells.UpdateAmmo(local.Ammo);
        _parry.Present(
            AbilityState(local.IsAlive, local.ParryTicks > 0, local.ParryCooldown),
            local.ParryCooldown);
        _rope.Present(
            AbilityState(local.IsAlive, local.Rope != RopeMode.NONE, local.RopeCooldown),
            local.RopeCooldown);

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

    private static AbilityIconState AbilityState(bool alive, bool active, int cooldown)
    {
        if (!alive)
            return AbilityIconState.UNAVAILABLE;
        if (active)
            return AbilityIconState.ACTIVE;
        return cooldown > 0 ? AbilityIconState.UNAVAILABLE : AbilityIconState.AVAILABLE;
    }
}
