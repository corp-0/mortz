using Godot;

namespace Mortz.Client.Match.PlayerHud;

public enum AbilityIconState
{
    AVAILABLE,
    UNAVAILABLE,
    ACTIVE,
}

[GlobalClass]
[Tool]
public partial class AbilityIcon : Panel
{
    [ExportCategory("Configuration")]
    [Export] private Color _borderAvailable;
    [Export] private Color _borderUnavailable;
    [Export] private Color _active;

    private Texture2D? _iconTexture;
    private StyleBoxFlat? _borderStyle;
    private AbilityIconState _state = AbilityIconState.AVAILABLE;

    [ExportToolButton("Available")]
    private Callable PreviewAvailableButton => Callable.From(PreviewAvailable);

    [ExportToolButton("Unavailable")]
    private Callable PreviewUnavailableButton => Callable.From(PreviewUnavailable);

    [ExportToolButton("Active")]
    private Callable PreviewActiveButton => Callable.From(PreviewActive);

    [Export]
    private Texture2D? Icon
    {
        get => _iconTexture;
        set
        {
            _iconTexture = value;
            _icon?.Texture = value;
        }
    }

    [ExportCategory("Internal")]
    [Export] private TextureRect? _icon;
    [Export] private TextureProgressBar? _cooldown;
    [Export] private Panel? _border;

    private int _cooldownTotal;

    public override void _Ready()
    {
        _icon?.Texture = _iconTexture;
        ApplyState();
    }

    private void SetState(AbilityIconState state)
    {
        _state = state;
        ApplyState();
    }

    public void Present(AbilityIconState state, int cooldownTicks)
    {
        cooldownTicks = Math.Max(0, cooldownTicks);
        if (cooldownTicks == 0)
        {
            _cooldownTotal = 0;
        }
        else if (cooldownTicks > _cooldownTotal)
        {
            _cooldownTotal = cooldownTicks;
        }

        if (_cooldown != null)
        {
            _cooldown.Value = _cooldownTotal == 0
                ? 0
                : cooldownTicks / (double)_cooldownTotal;
            _cooldown.Visible = cooldownTicks > 0;
        }

        SetState(state);
    }

    private void PreviewAvailable() => SetState(AbilityIconState.AVAILABLE);

    private void PreviewUnavailable() => SetState(AbilityIconState.UNAVAILABLE);

    private void PreviewActive() => SetState(AbilityIconState.ACTIVE);

    private void ApplyState()
    {
        StyleBoxFlat? style = BorderStyle();

        style?.BorderColor = _state switch
        {
            AbilityIconState.AVAILABLE => _borderAvailable,
            AbilityIconState.UNAVAILABLE => _borderUnavailable,
            AbilityIconState.ACTIVE => _active,
            _ => _borderUnavailable,
        };
    }

    private StyleBoxFlat? BorderStyle()
    {
        if (_borderStyle != null)
        {
            return _borderStyle;
        }

        if (_border == null || GetThemeStylebox("panel") is not StyleBoxFlat source)
        {
            return null;
        }

        StyleBoxFlat background = (StyleBoxFlat)source.Duplicate();
        background.BorderColor = background.BgColor;
        background.AntiAliasing = false;
        AddThemeStyleboxOverride("panel", background);

        _borderStyle = (StyleBoxFlat)source.Duplicate();
        _borderStyle.BgColor = Colors.Transparent;
        _border.AddThemeStyleboxOverride("panel", _borderStyle);
        return _borderStyle;
    }
}
