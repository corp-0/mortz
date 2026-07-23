using Godot;

namespace Mortz.Client.Menus.MainMenuAmbient;

public partial class MainMenuBackground : SubViewportContainer
{
    private const float AMBIENT_FADE_START_DB = -50.0f;
    private static readonly StringName _startSequenceName = "start_sequence";

    [Export] private AnimationPlayer _player = null!;
    [Export] private AudioStreamPlayer _ambientLoop = null!;
    [Export(PropertyHint.Range, "0.1,10,0.1")]
    private float _ambientFadeSeconds = 2.5f;

    private bool _hasStarted;

    public override void _Ready()
    {
        float targetVolumeDb = _ambientLoop.VolumeDb;
        _ambientLoop.VolumeDb = AMBIENT_FADE_START_DB;
        _ambientLoop.Play();

        CreateTween().TweenProperty(
            _ambientLoop,
            new NodePath("volume_db"),
            targetVolumeDb,
            _ambientFadeSeconds
        );
    }

    public void StartSequence()
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        _player.Play(_startSequenceName);
    }
}
