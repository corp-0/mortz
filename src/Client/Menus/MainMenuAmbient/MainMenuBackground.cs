using Godot;

namespace Mortz.Client.Menus.MainMenuAmbient;

public partial class MainMenuBackground : SubViewportContainer
{
    private static readonly StringName _startSequenceName = "start_sequence";

    [Export] private AnimationPlayer _player = null!;
    private bool _hasStarted;

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
