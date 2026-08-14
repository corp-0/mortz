using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Replication;
using Mortz.Core.Sim;

namespace Mortz.Client.Debug;

public partial class KillingSpreeDebug : Node2D
{
    private const float FLOOR_HEIGHT = 120f;

    [Export] private PlayerView _player = null!;
    [Export] private Label _tierLabel = null!;

    private readonly PlayerStats _stats = PlayerStats.Resolve(new MatchConfig());
    private Vector2 _feet;
    private Vector2 _velocity;
    private float _groundY;
    private byte _aim;
    private byte _magnitude = 5;

    public override void _Ready()
    {
        _player.SetSfx(new NullSfx());
        _player.Configure(_stats);
        _player.SetIsLocal(true);
        _player.SetPlayerName("Preview");
        ResizeWorld();
        _feet = new Vector2(GetViewportRect().Size.X * 0.5f, _groundY);
        SetMagnitude(5, "BLOODLUST");
        ApplyPlayer();
    }

    public override void _PhysicsProcess(double delta)
    {
        ResizeWorld();
        float elapsed = (float)delta;
        float direction = Input.GetAxis("move_left", "move_right");
        _velocity.X = Mathf.MoveToward(
            _velocity.X,
            direction * SimConfig.MAX_RUN_SPEED,
            SimConfig.GROUND_ACCEL * elapsed);

        bool grounded = _feet.Y >= _groundY;
        if (grounded && Input.IsActionJustPressed("jump"))
        {
            _velocity.Y = -SimConfig.JUMP_SPEED;
            grounded = false;
        }
        if (!grounded)
        {
            _velocity.Y = MathF.Min(
                _velocity.Y + SimConfig.GRAVITY * elapsed,
                SimConfig.MAX_FALL_SPEED);
        }

        _feet += _velocity * elapsed;
        Vector2 viewportSize = GetViewportRect().Size;
        _feet.X = Math.Clamp(
            _feet.X,
            SimConfig.PLAYER_HALF_WIDTH,
            viewportSize.X - SimConfig.PLAYER_HALF_WIDTH);
        if (_feet.Y >= _groundY)
        {
            _feet.Y = _groundY;
            _velocity.Y = 0f;
        }

        Vector2 bodyCenter = _feet - Vector2.Up * SimConfig.PLAYER_HALF_HEIGHT;
        Vector2 aimVector = GetGlobalMousePosition() - bodyCenter;
        if (aimVector.LengthSquared() > 1f)
        {
            _aim = PlayerInput.AimFromVector(new Vec2(aimVector.X, aimVector.Y));
        }
        ApplyPlayer();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("101426"));
        DrawRect(
            new Rect2(0f, _groundY, size.X, size.Y - _groundY),
            new Color("252b46"));
        DrawLine(
            new Vector2(0f, _groundY),
            new Vector2(size.X, _groundY),
            new Color("6973a8"),
            2f);
    }

    private void OnOff() => SetMagnitude(0, "OFF");
    private void OnBloodlust() => SetMagnitude(5, "5 — BLOODLUST");
    private void OnPunishment() => SetMagnitude(7, "7 — PUNISHMENT");
    private void OnDominating() => SetMagnitude(9, "9 — DOMINATING");
    private void OnMachineGod() => SetMagnitude(11, "11 — MACHINE GOD");
    private void OnPsycho() => SetMagnitude(13, "13 — FUCKING PSYCHO");

    private void SetMagnitude(int magnitude, string label)
    {
        _magnitude = (byte)magnitude;
        _tierLabel.Text = label;
        ApplyPlayer();
    }

    private void ResizeWorld()
    {
        float groundY = GetViewportRect().Size.Y - FLOOR_HEIGHT;
        if (Mathf.IsEqualApprox(_groundY, groundY))
        {
            return;
        }
        _groundY = groundY;
        QueueRedraw();
    }

    private void ApplyPlayer() => _player.Apply(new PlayerViewState(
        Feet: _feet,
        Aim: _aim,
        Skin: 0,
        Ammo: _stats.MaxAmmo,
        ReloadTicks: 0,
        Health: _stats.MaxHealth,
        RespawnTicks: 0,
        ParryTicks: 0,
        DashCooldown: 0,
        SpawnImmunityTicks: 0,
        Presentation: new PlayerPresentationState { KillingSpreeMagnitude = _magnitude }),
        playTransitions: false);
}
