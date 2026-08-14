using Godot;

namespace Mortz.Client.Views;

[GlobalClass]
public partial class BleedingEffect : PlayerVfx
{
    [Export] private Sprite2D _wounds = null!;
    [Export] private CpuParticles2D _blood = null!;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    private float _minimumBurstInterval = 0.7f;

    [Export(PropertyHint.Range, "0.1,5.0,0.1")]
    private float _maximumBurstInterval = 1.4f;

    private readonly RandomNumberGenerator _random = new();
    private bool _active;
    private float _untilBurst;

    public bool Active => _active;

    public override void _Ready()
    {
        _random.Randomize();
        ((ShaderMaterial)_wounds.Material).SetShaderParameter(
            "seed", _random.RandfRange(0f, 1000f));
        Visible = false;
        SetProcess(false);
    }

    public override void Apply(in PlayerViewState state, in PlayerVisualPose pose)
    {
        pose.ApplyBody(_wounds);
        SetActive(state.Presentation.IsBleeding);
    }

    public override void _Process(double delta)
    {
        _untilBurst -= (float)delta;
        if (_untilBurst > 0f)
        {
            return;
        }

        _blood.Restart();
        ScheduleBurst();
    }

    private void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        Visible = active;
        SetProcess(active);
        if (active)
        {
            ScheduleBurst();
        }
        else
        {
            _blood.Emitting = false;
        }
    }

    private void ScheduleBurst() =>
        _untilBurst = _random.RandfRange(
            _minimumBurstInterval,
            MathF.Max(_minimumBurstInterval, _maximumBurstInterval));
}
