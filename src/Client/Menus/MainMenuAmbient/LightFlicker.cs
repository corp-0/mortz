using Godot;
using Mortz.Extensions;

namespace Mortz.Client.Menus.MainMenuAmbient;

public partial class LightFlicker : Node3D
{
    [Export] private bool _isMainLight;
    [Export] private bool _isAllowedToRandomFlick;
    [Export] private Node3D _root = null!;

    [Export] private float _minLitTime = 4.0f;
    [Export] private float _maxLitTime = 9.0f;

    [Export] private int _minFlicks = 1;
    [Export] private int _maxFlicks = 3;
    [Export] private float _minFlickDark = 0.04f;
    [Export] private float _maxFlickDark = 0.12f;
    [Export] private float _minFlickLit = 0.04f;
    [Export] private float _maxFlickLit = 0.10f;

    [Export(PropertyHint.Range, "0,1")] private float _blackoutChance = 0.25f;
    [Export] private float _minDarkTime = 1.5f;
    [Export] private float _maxDarkTime = 4.0f;

    private Light3D _light = null!;
    private const float TARGET_DIRECTIONAL_ENERGY = 0.02f;
    private float _timeLeft;
    private int _flicksLeft;
    private bool _inBlackout;
    private DirectionalLight3D? _ambientLight;

    // animation call-method key at the end of the start sequence
    public void BeginRandomFlicker()
    {
        _timeLeft = RandRange(_minLitTime, _maxLitTime);
        _isAllowedToRandomFlick = true;
    }

    public void DoRandomFlick(double delta)
    {
        _timeLeft -= (float)delta;
        if (_timeLeft > 0.0f) return;

        if (_inBlackout)
        {
            _inBlackout = false;
            Flick();
            _timeLeft = RandRange(_minLitTime, _maxLitTime);
            return;
        }

        if (_flicksLeft == 0) _flicksLeft = 2 * GD.RandRange(_minFlicks, _maxFlicks);

        Flick();
        _flicksLeft--;

        if (_flicksLeft > 0)
        {
            _timeLeft = _light.Visible
                ? RandRange(_minFlickLit, _maxFlickLit)
                : RandRange(_minFlickDark, _maxFlickDark);
            return;
        }

        if (GD.Randf() < _blackoutChance)
        {
            _inBlackout = true;
            Flick();
            _timeLeft = RandRange(_minDarkTime, _maxDarkTime);
            return;
        }

        _timeLeft = RandRange(_minLitTime, _maxLitTime);
    }

    private static float RandRange(float min, float max) => GD.Randf() * (max - min) + min;

    private void Flick()
    {
        bool lit = !_light.Visible;
        Visible = lit;
        _light.Visible = lit;
        _ambientLight?.LightEnergy = lit ? TARGET_DIRECTIONAL_ENERGY : 0.0f;
    }

    public override void _Ready()
    {
        _light = this.GetByTypeOrNull<Light3D>() ?? throw new ArgumentNullException();
        _timeLeft = RandRange(_minLitTime, _maxLitTime);
        if (_isMainLight)
        {
            _ambientLight = _root.GetByTypeOrNull<DirectionalLight3D>();
        }
    }

    public override void _Process(double delta)
    {
        if (!_isAllowedToRandomFlick) return;
        DoRandomFlick(delta);
    }
}
