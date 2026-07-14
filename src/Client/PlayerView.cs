using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>
/// Visual shell for one player, local or remote. GameView owns the lifecycle
/// and pushes state in through Apply every frame; nothing here simulates or
/// talks to the network. The scene root sits at the body center, so position
/// math converts from the sim's feet-midpoint convention.
/// </summary>
public partial class PlayerView : Node2D
{
    /// <summary>Outline every player's sim collision box (F3 toggles it in GameView).</summary>
    public static bool DrawSimBoxes;

    private const float HIT_FLASH_TIME = 0.2f; // s
    // local dashCooldown is predicted, so reconciliation can nudge it up a few
    // ticks with no dash; a real dash raises it by the full cooldown.
    private const int DASH_CORRECTION_SLACK = 5; // ticks

    [Export] private Sprite2D _body = null!;
    [Export] private Node2D _aimPivot = null!;
    [Export] private Sprite2D _launcher = null!;
    [Export] private Camera2D _camera = null!;
    [Export] private ProgressBar _reloadBar = null!;
    [Export] private Label _nameplate = null!;
    [Export] private CpuParticles2D _dashDust = null!;

    private static readonly Color _shieldColor = new(0.4f, 0.9f, 1f, 0.8f);

    private bool _boxVisible;
    private bool _shieldVisible;
    private bool _isLocal;
    private int _lastHealth = -1;
    private int _lastDashCooldown = -1;
    private float _hitFlash;
    private PlayerStats _stats = null!;

    /// <summary>Must be called before the first Apply (PlayerViewManager does).</summary>
    public void Configure(PlayerStats stats) => _stats = stats;

    /// <summary>Only the local player's camera drives the screen, and only
    /// remote players wear a nameplate; you know who you are.</summary>
    public void SetIsLocal(bool isLocal)
    {
        _isLocal = isLocal;
        _camera.Enabled = isLocal;
        _nameplate.Visible = !isLocal;
    }

    public void SetPlayerName(string name) => _nameplate.Text = name;

    public void Apply(Vector2 feet, byte aim, byte skin, byte ammo, byte reloadTicks, byte health,
        byte respawnTicks, byte parryTicks, byte dashCooldown)
    {
        if ((parryTicks > 0) != _shieldVisible)
        {
            _shieldVisible = parryTicks > 0;
            QueueRedraw();
        }
        // Dead = gibbed: no body to show. Position keeps tracking so the local
        // player's camera lingers on the death spot until the respawn.
        Visible = respawnTicks == 0;
        UpdateReloadBar(ammo, reloadTicks);
        UpdateHealth(health);
        Position = new Vector2(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);

        // DashCooldown only ever jumps up the tick a dash fires, then decays.
        // A rising edge is that launch; ignore the first frame so a player
        // already mid-cooldown when they come into view doesn't puff. The local
        // player is predicted, so reconciliation can re-raise the value by a few
        // ticks with no dash; require the rise to clear the slack for them only.
        int dashRise = dashCooldown - _lastDashCooldown;
        int minRise = _isLocal ? DASH_CORRECTION_SLACK : 1;
        if (_lastDashCooldown >= 0 && dashRise >= minRise)
            _dashDust.Restart();
        _lastDashCooldown = dashCooldown;
        _body.Frame = skin % SimConfig.SKIN_COUNT;

        Vec2 aimDir = PlayerInput.AimToDir(aim);
        _aimPivot.Rotation = MathF.Atan2(aimDir.Y, aimDir.X);
        // Rotation alone leaves the launcher upside down past 90 degrees;
        // flipping it across the barrel axis keeps the art upright while the
        // barrel stays on the aim. The body just faces wherever you aim.
        bool aimingLeft = aimDir.X < 0f;
        _body.FlipH = aimingLeft;
        _launcher.FlipV = aimingLeft;

        if (DrawSimBoxes != _boxVisible)
            QueueRedraw();
    }

    /// <summary>
    /// Visible only while a reload runs. The range is pinned when the bar
    /// appears (min = shells held then, max = a full magazine), so a one-shell
    /// top-up sweeps the same visual distance as a five-shell reload sweeps
    /// per shell. Interruption and completion both zero ReloadTicks, which
    /// hides it again.
    /// </summary>
    private void UpdateReloadBar(byte ammo, byte reloadTicks)
    {
        if (reloadTicks == 0)
        {
            _reloadBar.Visible = false;
            return;
        }
        if (!_reloadBar.Visible)
        {
            _reloadBar.MinValue = ammo;
            _reloadBar.MaxValue = _stats.MaxAmmo;
            _reloadBar.Visible = true;
        }
        // Shells banked plus the fraction of the one being loaded.
        _reloadBar.Value = ammo + 1.0 - (double)reloadTicks / _stats.ReloadTicks;
    }

    /// <summary>Red sprite flash whenever health drops. No bar: hurt state
    /// will be worn by the sprite itself eventually (wounds). Respawns raise
    /// health, so they never flash.</summary>
    private void UpdateHealth(byte health)
    {
        if (_lastHealth >= 0 && health < _lastHealth)
            _hitFlash = HIT_FLASH_TIME;
        _lastHealth = health;
    }

    public override void _Process(double delta)
    {
        if (_hitFlash <= 0f)
            return;
        _hitFlash = MathF.Max(0f, _hitFlash - (float)delta);
        _body.Modulate = Colors.White.Lerp(Colors.Red, _hitFlash / HIT_FLASH_TIME);
    }

    public override void _Draw()
    {
        // Placeholder parry bubble until real shield art exists.
        if (_shieldVisible)
            DrawArc(Vector2.Zero, _stats.ParryRadius, 0, MathF.Tau, 48, _shieldColor, 2f, antialiased: true);
        _boxVisible = DrawSimBoxes;
        if (!DrawSimBoxes)
            return;
        DrawRect(new Rect2(
                -SimConfig.PLAYER_HALF_WIDTH, -SimConfig.PLAYER_HALF_HEIGHT,
                SimConfig.PLAYER_HALF_WIDTH * 2, SimConfig.PLAYER_HALF_HEIGHT * 2),
            Colors.Lime, filled: false);
    }
}
