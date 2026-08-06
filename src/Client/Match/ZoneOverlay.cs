using Godot;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Client.Match;

/// <summary>Debug/editor rendering for authored zones. GameMap hides it during
/// normal play; an editor can show the node while creating zones.</summary>
public partial class ZoneOverlay : Node2D
{
    private static readonly Color _effectFill = new(0.35f, 0.65f, 1f, 0.10f);
    private static readonly Color _effectLine = new(0.35f, 0.65f, 1f, 0.35f);
    private static readonly Color _tagFill = new(1f, 1f, 1f, 0.04f);
    private static readonly Color _tagLine = new(1f, 1f, 1f, 0.15f);

    private MapZones _zones = MapZones.None;

    public void Initialize(MapZones zones) => _zones = zones;

    public override void _Draw()
    {
        foreach (MapZone zone in _zones.All)
        {
            bool hasEffects = zone.Effects != null;
            Color fill = hasEffects ? _effectFill : _tagFill;
            Color line = hasEffects ? _effectLine : _tagLine;
            ZoneShape shape = zone.Shape;
            if (shape.Kind == ZoneShapeKind.RECT)
            {
                Rect2 rect = new(shape.X, shape.Y, shape.Width, shape.Height);
                DrawRect(rect, fill);
                DrawRect(rect, line, filled: false);
            }
            else
            {
                Vector2 center = new(shape.X, shape.Y);
                DrawCircle(center, shape.Radius, fill);
                DrawArc(center, shape.Radius, 0, Mathf.Tau, 64, line);
            }
        }
    }
}
