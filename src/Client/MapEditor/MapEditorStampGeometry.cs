using System.Collections.Immutable;

namespace Mortz.Client.MapEditor;

public static class MapEditorStampGeometry
{
    public static MapEditorPoint SnapToCell(MapEditorPoint point, MapEditorSnap snap)
    {
        if (snap == MapEditorSnap.NONE)
            return point;
        int interval = (int)snap;
        return new MapEditorPoint(CellOrigin(point.X, interval), CellOrigin(point.Y, interval));
    }

    public static IEnumerable<MapEditorPoint> CellsAlongStroke(MapEditorPoint from,
        MapEditorPoint to, MapEditorSnap snap)
    {
        int interval = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        MapEditorPoint start = SnapToCell(from, snap);
        MapEditorPoint end = SnapToCell(to, snap);
        long x = start.X / interval;
        long y = start.Y / interval;
        long endX = end.X / interval;
        long endY = end.Y / interval;
        long dx = Math.Abs(endX - x);
        long stepX = x < endX ? 1 : -1;
        long dy = -Math.Abs(endY - y);
        long stepY = y < endY ? 1 : -1;
        long error = dx + dy;
        while (true)
        {
            yield return new MapEditorPoint(checked((int)(x * interval)),
                checked((int)(y * interval)));
            if (x == endX && y == endY)
                yield break;
            long twiceError = 2 * error;
            if (twiceError >= dy)
            {
                error += dy;
                x += stepX;
            }
            if (twiceError <= dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    public static MapEditorBrushDraft CreateTemplate(MapEditorBrush brush)
    {
        MapEditorPoint anchor = Anchor(brush.Shape);
        return new MapEditorBrushDraft(brush.Name, brush.Layer,
            Translate(brush.Shape, checked(-anchor.X), checked(-anchor.Y)), brush.Material,
            brush.Projection with
            {
                Origin = new MapEditorPoint(
                    checked(brush.Projection.Origin.X - anchor.X),
                    checked(brush.Projection.Origin.Y - anchor.Y)),
            }, brush.Visible);
    }

    public static MapEditorBrushDraft Place(MapEditorStamp stamp, MapEditorPoint point,
        string name)
    {
        MapEditorBrushDraft template = stamp.Brush;
        return template with
        {
            Name = name,
            Shape = Translate(template.Shape, point.X, point.Y),
            Projection = template.Projection with
            {
                Origin = new MapEditorPoint(
                    checked(template.Projection.Origin.X + point.X),
                    checked(template.Projection.Origin.Y + point.Y)),
            },
        };
    }

    private static MapEditorPoint Anchor(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => new MapEditorPoint(rect.X, rect.Y),
        MapEditorEllipseBrushShape ellipse => new MapEditorPoint(ellipse.X, ellipse.Y),
        MapEditorPolygonBrushShape polygon when !polygon.Vertices.IsEmpty => new MapEditorPoint(
            polygon.Vertices.Min(point => point.X), polygon.Vertices.Min(point => point.Y)),
        _ => default,
    };

    private static int CellOrigin(int value, int interval)
    {
        int quotient = Math.DivRem(value, interval, out int remainder);
        if (remainder < 0)
            quotient--;
        return checked(quotient * interval);
    }

    private static MapEditorBrushShape Translate(MapEditorBrushShape shape, int x, int y) =>
        shape switch
        {
            MapEditorRectBrushShape rect => rect with
            {
                X = checked(rect.X + x),
                Y = checked(rect.Y + y),
            },
            MapEditorEllipseBrushShape ellipse => ellipse with
            {
                X = checked(ellipse.X + x),
                Y = checked(ellipse.Y + y),
            },
            MapEditorPolygonBrushShape polygon => new MapEditorPolygonBrushShape(
                polygon.Vertices.Select(point => new MapEditorPoint(
                    checked(point.X + x), checked(point.Y + y))).ToImmutableArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
}
