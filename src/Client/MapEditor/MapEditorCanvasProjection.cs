using Godot;
using Mortz.Content;
using SimVec2 = Mortz.Core.Sim.Vec2;

namespace Mortz.Client.MapEditor;

public static class MapEditorCanvasProjection
{
    public static Rect2 SpawnBody(MapSpawnPoint spawn) =>
        new(spawn.X - 16f, spawn.Y - 32f, 32f, 32f);

    public static Vector2[] BrushOutline(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => Rect(rect),
        MapEditorEllipseBrushShape ellipse => Ellipse(ellipse.X, ellipse.Y,
            ellipse.RadiusX, ellipse.RadiusY, ellipse.Rotation),
        MapEditorPolygonBrushShape polygon => polygon.Vertices.Select(vertex =>
            new Vector2(vertex.X, vertex.Y)).ToArray(),
        _ => [],
    };

    public static Vector2[] MaterialQuad(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => Rect(rect),
        MapEditorEllipseBrushShape ellipse => EllipseQuad(ellipse),
        MapEditorPolygonBrushShape polygon => polygon.Vertices.Select(vertex =>
            new Vector2(vertex.X, vertex.Y)).ToArray(),
        _ => [],
    };

    public static Vector2 PreviewUv(MapEditorBrush brush, MapEditorTextureData texture,
        Vector2 point)
    {
        if (brush.Projection.Mode == MapEditorProjectionMode.REPEAT)
        {
            SimVec2 uv = MapEditorGeometry.RepeatPreviewUv(new SimVec2(point.X, point.Y),
                brush.Projection, texture.Width, texture.Height);
            return new Vector2(uv.X, uv.Y);
        }

        return brush.Shape switch
        {
            MapEditorRectBrushShape rect => RectStretchUv(rect, point),
            MapEditorEllipseBrushShape ellipse => EllipseStretchUv(ellipse, point),
            MapEditorPolygonBrushShape polygon => PolygonStretchUv(polygon, point),
            _ => Vector2.Zero,
        };
    }

    public static Vector2[] ZoneRect(RectMapZoneShape rect, Func<Vector2, Vector2> mapToLocal) =>
        Rect(rect.X, rect.Y, rect.Width, rect.Height, rect.Rotation)
            .Select(mapToLocal).ToArray();

    public static Vector2[] ZoneEllipse(int x, int y, int radiusX, int radiusY,
        float rotation, Func<Vector2, Vector2> mapToLocal) =>
        Ellipse(x, y, radiusX, radiusY, rotation).Select(mapToLocal).ToArray();

    private static Vector2[] Rect(MapEditorRectBrushShape rect) =>
        Rect(rect.X, rect.Y, rect.Width, rect.Height, rect.Rotation);

    private static Vector2[] Rect(int x, int y, int width, int height, float rotationDegrees)
    {
        Vector2 center = new(x + width / 2f, y + height / 2f);
        float rotation = Mathf.DegToRad(rotationDegrees);
        Vector2[] points =
        [
            new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height),
        ];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = center + (points[index] - center).Rotated(rotation);
        }
        return points;
    }

    private static Vector2[] Ellipse(int x, int y, int radiusX, int radiusY,
        float rotationDegrees)
    {
        const int SEGMENTS = 64;
        Vector2[] points = new Vector2[SEGMENTS];
        Vector2 center = new(x, y);
        float rotation = Mathf.DegToRad(rotationDegrees);
        for (int index = 0; index < points.Length; index++)
        {
            float angle = Mathf.Tau * index / SEGMENTS;
            points[index] = center + new Vector2(Mathf.Cos(angle) * radiusX,
                Mathf.Sin(angle) * radiusY).Rotated(rotation);
        }
        return points;
    }

    private static Vector2[] EllipseQuad(MapEditorEllipseBrushShape ellipse)
    {
        Vector2 center = new(ellipse.X, ellipse.Y);
        float rotation = Mathf.DegToRad(ellipse.Rotation);
        Vector2[] points =
        [
            new(-ellipse.RadiusX, -ellipse.RadiusY),
            new(ellipse.RadiusX, -ellipse.RadiusY),
            new(ellipse.RadiusX, ellipse.RadiusY),
            new(-ellipse.RadiusX, ellipse.RadiusY),
        ];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = center + points[index].Rotated(rotation);
        }
        return points;
    }

    private static Vector2 RectStretchUv(MapEditorRectBrushShape rect, Vector2 point)
    {
        Vector2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        Vector2 local = center + (point - center).Rotated(-Mathf.DegToRad(rect.Rotation));
        return new Vector2((local.X - rect.X) / rect.Width, (local.Y - rect.Y) / rect.Height);
    }

    private static Vector2 EllipseStretchUv(MapEditorEllipseBrushShape ellipse, Vector2 point)
    {
        Vector2 center = new(ellipse.X, ellipse.Y);
        Vector2 local = (point - center).Rotated(-Mathf.DegToRad(ellipse.Rotation));
        return new Vector2((local.X + ellipse.RadiusX) / (ellipse.RadiusX * 2f),
            (local.Y + ellipse.RadiusY) / (ellipse.RadiusY * 2f));
    }

    private static Vector2 PolygonStretchUv(MapEditorPolygonBrushShape polygon, Vector2 point)
    {
        MapEditorBounds bounds = MapEditorGeometry.Bounds(polygon);
        return new Vector2((point.X - bounds.Left) / (bounds.Right - bounds.Left),
            (point.Y - bounds.Top) / (bounds.Bottom - bounds.Top));
    }
}
