using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Ui;
using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;

namespace Mortz.Client.Views;

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
    private const int IMMUNITY_TOGGLES_PER_SECOND = 10;
    private const byte IMMUNITY_FLICKER_TICKS = SimConfig.TICK_RATE / IMMUNITY_TOGGLES_PER_SECOND;

    [Export] private Sprite2D _body = null!;
    [Export] private Node2D _aimPivot = null!;
    [Export] private Sprite2D _launcher = null!;
    [Export] private PlayerReloadIndicator _reloadBar = null!;
    [Export] private Label _nameplate = null!;
    [Export] private CpuParticles2D _dashDust = null!;
    [Export] private AnimatedSprite2D _typingAnimation = null!;

    private static readonly Color _shieldColor = new(0.4f, 0.9f, 1f, 0.8f);

    public PlayerStats StatsForTest { get; private set; } = null!;

    private ISfx _sfx = null!;
    private bool _boxVisible;
    private bool _shieldVisible;
    private bool _isLocal;
    private float _hitFlash;
    private PlayerViewState? _previous;
    private SfxHandle _reloadSound;

    public void SetSfx(ISfx sfx) => _sfx = sfx;

    /// <summary>Called before the first Apply (PlayerViewManager does) and again
    /// whenever this player's replicated stats change.</summary>
    public void Configure(PlayerStats stats)
    {
        StatsForTest = stats;
        _reloadBar.Configure(stats);
        QueueRedraw(); // the shield can be up while its radius changes
    }

    /// <summary>Only remote players wear a nameplate; you know who you are.</summary>
    public void SetIsLocal(bool isLocal)
    {
        _isLocal = isLocal;
        _nameplate.Visible = !isLocal;
    }

    public void SetPlayerName(string name) => _nameplate.Text = name;

    /// <summary>The chat balloon; everyone sees it, including the typist.</summary>
    public void SetTyping(bool typing)
    {
        if (_typingAnimation.Visible == typing)
            return;
        _typingAnimation.Visible = typing;
        if (typing)
            _typingAnimation.Play();
        else
            _typingAnimation.Stop();
    }

    /// <summary>Nameplates wear the team color; body tint stays free for the
    /// hit flash. No team means the neutral color, as when teams turn off.</summary>
    public void SetTeam(Team? team) =>
        _nameplate.Modulate = team is Team assigned ? TeamColors.For(assigned) : Colors.White;

    public override void _ExitTree() => _reloadSound.Stop();

    public void Apply(in PlayerViewState next, bool playTransitions = true)
    {
        if (playTransitions && _previous is PlayerViewState previous)
            PlayTransitions(PlayerViewTransitions.Between(previous, next, _isLocal));

        if ((next.ParryTicks > 0) != _shieldVisible)
        {
            _shieldVisible = next.ParryTicks > 0;
            QueueRedraw();
        }
        // Dead = gibbed: no body to show. Position keeps tracking so the local
        // player's camera lingers on the death spot until the respawn.
        Visible = next.RespawnTicks == 0;
        bool spawnProtectedVisible = SpawnProtectedSpriteVisible(next.SpawnImmunityTicks);
        _body.Visible = spawnProtectedVisible;
        _aimPivot.Visible = spawnProtectedVisible;
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

    public static bool SpawnProtectedSpriteVisible(byte immunityTicks) =>
        immunityTicks == 0 || (immunityTicks / IMMUNITY_FLICKER_TICKS & 1) == 0;

    /// <summary>Everything that fires once on an edge rather than tracking a
    /// value, so none of it can run before there is a frame to compare to.</summary>
    private void PlayTransitions(PlayerViewTransition transitions)
    {
        if (transitions.HasFlag(PlayerViewTransition.PARRY_RAISED))
            _sfx.PlayAttached(_sfx.Sounds.ParryRaise, this);
        if (transitions.HasFlag(PlayerViewTransition.SHELL_RELOAD_STARTED))
        {
            _reloadSound.Stop();
            _reloadSound = _sfx.PlayAttached(_sfx.Sounds.MortarReload, this);
        }
        else if (transitions.HasFlag(PlayerViewTransition.RELOAD_STOPPED))
        {
            _reloadSound.Stop();
            _reloadSound = default;
        }
        if (transitions.HasFlag(PlayerViewTransition.DASHED))
            _dashDust.Restart();
        if (transitions.HasFlag(PlayerViewTransition.TOOK_DAMAGE))
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
            DrawArc(Vector2.Zero, StatsForTest.ParryRadius, 0, MathF.Tau, 48, _shieldColor, 2f, antialiased: true);
        _boxVisible = DrawSimBoxes;
        if (!DrawSimBoxes)
            return;
        DrawRect(new Rect2(
                -SimConfig.PLAYER_HALF_WIDTH, -SimConfig.PLAYER_HALF_HEIGHT,
                SimConfig.PLAYER_HALF_WIDTH * 2, SimConfig.PLAYER_HALF_HEIGHT * 2),
            Colors.Lime, filled: false);
    }
}
