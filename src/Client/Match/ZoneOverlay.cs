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
            Vector2[] points = shape.Kind == ZoneShapeKind.RECT
                ? RectPoints(shape)
                : EllipsePoints(shape);
            DrawColoredPolygon(points, fill);
            Vector2[] outline = new Vector2[points.Length + 1];
            points.CopyTo(outline, 0);
            outline[^1] = points[0];
            DrawPolyline(outline, line);
        }
    }

    private static Vector2[] RectPoints(ZoneShape shape)
    {
        Vector2 center = new(shape.X + shape.Width / 2f, shape.Y + shape.Height / 2f);
        float rotation = Mathf.DegToRad(shape.Rotation);
        Vector2[] points =
        [
            new(shape.X, shape.Y),
            new(shape.X + shape.Width, shape.Y),
            new(shape.X + shape.Width, shape.Y + shape.Height),
            new(shape.X, shape.Y + shape.Height),
        ];
        for (int i = 0; i < points.Length; i++)
            points[i] = center + (points[i] - center).Rotated(rotation);
        return points;
    }

    private static Vector2[] EllipsePoints(ZoneShape shape)
    {
        const int SEGMENTS = 64;
        Vector2[] points = new Vector2[SEGMENTS];
        Vector2 center = new(shape.X, shape.Y);
        float rotation = Mathf.DegToRad(shape.Rotation);
        for (int i = 0; i < SEGMENTS; i++)
        {
            float angle = Mathf.Tau * i / SEGMENTS;
            Vector2 offset = new(Mathf.Cos(angle) * shape.Radius,
                Mathf.Sin(angle) * shape.RadiusY);
            points[i] = center + offset.Rotated(rotation);
        }
        return points;
    }
}
