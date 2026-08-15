using Godot;

namespace Mortz.Client.Views;

/// <summary>Draws every visible rope on top of the world; refilled each frame by GameView.</summary>
public partial class RopeOverlay : Node2D
{
    private static readonly Color _ropeColor = new(0.85f, 0.78f, 0.6f);
    private static readonly Color _hookColor = new(0.7f, 0.7f, 0.75f);

    private readonly List<RopeSegment> _segments = [];

    public IReadOnlyList<RopeSegment> Segments => _segments;

    public void Apply(IReadOnlyList<RopeSegment> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        foreach (RopeSegment segment in _segments)
        {
            DrawLine(segment.From, segment.To, _ropeColor, 2);
            DrawRect(new Rect2(segment.To - new Vector2(2, 2), new Vector2(5, 5)), _hookColor);
        }
    }
}
