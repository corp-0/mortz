using Godot;
using Mortz.Core;
using Mortz.Core.Sim;

namespace Mortz.Client;

/// <summary>
/// The magazine as one shell pictogram per magazine slot, loaded ones lit and
/// spent ones ghosted: readable mid-fight where a number isn't. Same
/// placeholder art language as MortarView until real prefabs exist.
/// </summary>
public partial class ShellRow : Control
{
    private const float SPACING = 24f;
    private static readonly Color _body = new(0.85f, 0.85f, 0.85f);
    private static readonly Color _nose = new(0.95f, 0.6f, 0.2f);

    private int _maxAmmo = SimConfig.MORTAR_MAX_AMMO;
    private int _ammo = SimConfig.MORTAR_MAX_AMMO;

    public void SetMaxAmmo(int maxAmmo)
    {
        _maxAmmo = maxAmmo;
        _ammo = maxAmmo;
        CustomMinimumSize = new Vector2(_maxAmmo * SPACING, 30);
        QueueRedraw();
    }

    public void SetAmmo(byte ammo)
    {
        if (ammo == _ammo)
            return;
        _ammo = ammo;
        QueueRedraw();
    }

    public override void _Ready() =>
        CustomMinimumSize = new Vector2(_maxAmmo * SPACING, 30);

    public override void _Draw()
    {
        for (int i = 0; i < _maxAmmo; i++)
        {
            Vector2 at = new Vector2(i * SPACING + SPACING / 2, Size.Y * 0.7f);
            float alpha = i < _ammo ? 1f : 0.18f;
            DrawCircle(at, 8, Faded(_body, alpha));
            DrawLine(at, at + new Vector2(0, -14), Faded(_nose, alpha), 3);
        }
    }

    private static Color Faded(Color c, float alpha) => new(c.R, c.G, c.B, alpha);
}
