using Godot;

namespace Mortz.Client.Match.PlayerHud;

[GlobalClass]
public partial class HealthBar : HBoxContainer
{
    [Export] private Color _emptyHealth = new(0.9f, 0.18f, 0.16f);
    [Export] private Color _halfHealth = new(0.95f, 0.68f, 0.12f);
    [Export] private Color _fullHealth = new(0.2f, 0.76f, 0.32f);

    [Export] private ProgressBar _healthBar = null!;

    public int MaxHp { get; private set; }
    private StyleBoxFlat? _healthFill;

    public void Configure(int maxHp)
    {
        MaxHp = maxHp;
        _healthBar.MaxValue = maxHp;
    }

    public void UpdateHealthBar(int health)
    {
        EnsureHealthBarStyle();
        _healthBar.Value = health;
        float ratio = _healthBar.MaxValue <= 0
            ? 0
            : Math.Clamp(health / (float)_healthBar.MaxValue, 0, 1);
        _healthFill!.BgColor = ratio < 0.5f
            ? _emptyHealth.Lerp(_halfHealth, ratio * 2)
            : _halfHealth.Lerp(_fullHealth, (ratio - 0.5f) * 2);
    }

    private void EnsureHealthBarStyle()
    {
        if (_healthFill != null)
            return;

        _healthFill = RoundedStyle(_fullHealth);
        _healthBar.AddThemeStyleboxOverride("fill", _healthFill);
        _healthBar.AddThemeStyleboxOverride("background", RoundedStyle(new Color(0.08f, 0.09f, 0.1f, 0.85f)));
        _healthBar.AddThemeColorOverride("font_outline_color", Colors.Black);
        _healthBar.AddThemeConstantOverride("outline_size", 3);
    }

    private static StyleBoxFlat RoundedStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
    };
}
