using Godot;
using Mortz.Extensions;

namespace Mortz.Client.Match.PlayerHud;

[GlobalClass]
public partial class ShellCounter : HBoxContainer
{
    [Export] private PackedScene _shellIcon = null!;
    [Export] private Color _spentColor;
    [Export] private Color _availableColor;

    private readonly List<Control> _icons = [];

    public int MaxAmmo { get; private set; }

    public void Configure(int maxAmmo)
    {
        this.KillDescendants();
        _icons.Clear();
        MaxAmmo = maxAmmo;

        for (int i = 0; i < MaxAmmo; i++)
        {
            Control icon = _shellIcon.Instantiate<Control>();
            AddChild(icon);
            _icons.Add(icon);
        }
    }

    public void UpdateAmmo(int ammo)
    {
        int available = Math.Clamp(ammo, 0, _icons.Count);
        for (int i = 0; i < _icons.Count; i++)
        {
            _icons[i].Modulate = i < available ? _availableColor : _spentColor;
        }
    }
}
