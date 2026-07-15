using Godot;

namespace Mortz.Client;

/// <summary>Draws every visible rope on top of the world; refilled each frame by GameView.</summary>
public partial class RopeOverlay : Node2D
{
    private static readonly Color _ropeColor = new(0.85f, 0.78f, 0.6f);
    private static readonly Color _hookColor = new(0.7f, 0.7f, 0.75f);

    public readonly List<(Vector2 From, Vector2 To)> Segments = new();

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        foreach ((Vector2 from, Vector2 to) in Segments)
        {
            DrawLine(from, to, _ropeColor, 2);
            DrawRect(new Rect2(to - new Vector2(2, 2), new Vector2(5, 5)), _hookColor);
        }
    }
}
