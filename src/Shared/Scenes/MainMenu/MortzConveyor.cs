using Godot;

namespace Mortz.Shared.Scenes.MainMenu;

[Tool]
public partial class MortzConveyor : Node3D
{
    private const string SHELL_GROUP = "conveyor_shell";

    [Export] private float _speed = 1.15f;
    [Export] private float _travelLength = 12.0f;
    [Export] private ConveyorChainBed _chainBed = null!;
    [Export] public bool IsMoving { get; set; }

    private readonly List<Node3D> _shells = [];

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not Node3D item)
                continue;

            if (item.IsInGroup(SHELL_GROUP))
                _shells.Add(item);
        }
    }

    public override void _Process(double delta)
    {
        if (!IsMoving) return;
        float movement = _speed * (float)delta;
        float halfLength = _travelLength * 0.5f;
        _chainBed.Advance(movement);

        foreach (Node3D shell in _shells)
        {
            Vector3 position = shell.Position;
            position.X += movement;
            if (position.X > halfLength)
                position.X -= _travelLength;

            shell.Position = position;
        }
    }
}
