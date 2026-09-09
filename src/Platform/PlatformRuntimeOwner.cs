using Godot;

namespace Mortz.Platform;

public partial class PlatformRuntimeOwner : Node
{
    private Action? _advance;
    private Action? _stop;

    public void Initialize(Action advance, Action stop)
    {
        _advance = advance;
        _stop = stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta) => _advance?.Invoke();

    public override void _ExitTree()
    {
        _advance = null;
        _stop?.Invoke();
        _stop = null;
    }
}
