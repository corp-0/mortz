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

    [Export] private Sprite2D _body = null!;
    [Export] private Node2D _aimPivot = null!;
    [Export] private Sprite2D _launcher = null!;
    [Export] private Camera2D _camera = null!;
    [Export] private PlayerReloadIndicator _reloadBar = null!;
    [Export] private Label _nameplate = null!;
    [Export] private CpuParticles2D _dashDust = null!;

    private static readonly Color _shieldColor = new(0.4f, 0.9f, 1f, 0.8f);

    private bool _boxVisible;
    private bool _shieldVisible;
    private bool _isLocal;
    private bool _replayActive;
    private float _hitFlash;
    private PlayerStats _stats = null!;
    private PlayerViewState? _previous;
    private SfxHandle _reloadSound;

    /// <summary>Must be called before the first Apply (PlayerViewManager does).</summary>
    public void Configure(PlayerStats stats)
    {
        _stats = stats;
        _reloadBar.Configure(stats);
    }

    /// <summary>Only the local player's camera drives the screen, and only
    /// remote players wear a nameplate; you know who you are.</summary>
    public void SetIsLocal(bool isLocal)
    {
        _isLocal = isLocal;
        _camera.Enabled = isLocal && !_replayActive;
        _nameplate.Visible = !isLocal;
    }

    public void SetReplayActive(bool active)
    {
        _replayActive = active;
        _camera.Enabled = _isLocal && !active;
    }

    public void SetPlayerName(string name) => _nameplate.Text = name;

    public override void _ExitTree() => _reloadSound.Stop();

    public void Apply(in PlayerViewState next, bool playTransitions = true)
    {
        if (playTransitions && _previous is { } previous)
            PlayTransitions(PlayerViewTransitions.Between(previous, next, _isLocal));

        if ((next.ParryTicks > 0) != _shieldVisible)
        {
            _shieldVisible = next.ParryTicks > 0;
            QueueRedraw();
        }
        // Dead = gibbed: no body to show. Position keeps tracking so the local
        // player's camera lingers on the death spot until the respawn.
        Visible = next.RespawnTicks == 0;
        _reloadBar.Apply(next.Ammo, next.ReloadTicks);
        Position = new Vector2(next.Feet.X, next.Feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
        _body.Frame = next.Skin % SimConfig.SKIN_COUNT;

        Vec2 aimDir = PlayerInput.AimToDir(next.Aim);
        _aimPivot.Rotation = MathF.Atan2(aimDir.Y, aimDir.X);
        // Rotation alone leaves the launcher upside down past 90 degrees;
        // flipping it across the barrel axis keeps the art upright while the
        // barrel stays on the aim. The body just faces wherever you aim.
        bool aimingLeft = aimDir.X < 0f;
        _body.FlipH = aimingLeft;
        _launcher.FlipV = aimingLeft;

        if (DrawSimBoxes != _boxVisible)
            QueueRedraw();
        _previous = next;
    }

    /// <summary>Everything that fires once on an edge rather than tracking a
    /// value, so none of it can run before there is a frame to compare to.</summary>
    private void PlayTransitions(PlayerViewTransition transitions)
    {
        if (transitions.HasFlag(PlayerViewTransition.ParryRaised))
            Sfx.PlayAttached(Sfx.Sounds.ParryRaise, this);
        if (transitions.HasFlag(PlayerViewTransition.ShellReloadStarted))
        {
            _reloadSound.Stop();
            _reloadSound = Sfx.PlayAttached(Sfx.Sounds.MortarReload, this);
        }
        else if (transitions.HasFlag(PlayerViewTransition.ReloadStopped))
        {
            _reloadSound.Stop();
            _reloadSound = default;
        }
        if (transitions.HasFlag(PlayerViewTransition.Dashed))
            _dashDust.Restart();
        if (transitions.HasFlag(PlayerViewTransition.TookDamage))
            _hitFlash = HIT_FLASH_TIME;
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
