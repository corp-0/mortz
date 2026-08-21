using System.Collections.Immutable;
using Mortz.Content;
using Mortz.Core.Sim;

namespace Mortz.Client.MapEditor;

public enum MapZoneHandle
{
    NONE,
    MOVE,
    SCALE,
}

public enum MapEditorSnap
{
    NONE = 1,
    PIXELS_8 = 8,
    PIXELS_16 = 16,
    PIXELS_32 = 32,
}

public enum MapEditorRectHandle
{
    NONE,
    MOVE,
    TOP_LEFT,
    TOP_RIGHT,
    BOTTOM_RIGHT,
    BOTTOM_LEFT,
}

public enum MapEditorEllipseHandle
{
    NONE,
    MOVE,
    RESIZE,
}

public readonly record struct MapEditorBounds(float Left, float Top, float Right, float Bottom)
{
    public bool Intersects(MapEditorBounds other) => Left < other.Right && Right > other.Left &&
                                                     Top < other.Bottom && Bottom > other.Top;
}

public static class MapEditorGeometry
{
    public static MapEditorBounds Bounds(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => Bounds(rect),
        MapEditorEllipseBrushShape ellipse => Bounds(ellipse),
        MapEditorPolygonBrushShape polygon => Bounds(polygon),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    public static MapEditorBounds Bounds(MapEditorRectBrushShape rect)
    {
        Vec2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        Vec2[] corners =
        [
            new(rect.X, rect.Y), new(rect.X + rect.Width, rect.Y),
            new(rect.X + rect.Width, rect.Y + rect.Height),
            new(rect.X, rect.Y + rect.Height),
        ];
        Vec2[] rotated = corners.Select(corner =>
            Rotate(corner - center, rect.Rotation) + center).ToArray();
        return new MapEditorBounds(rotated.Min(point => point.X), rotated.Min(point => point.Y),
            rotated.Max(point => point.X), rotated.Max(point => point.Y));
    }

    public static MapEditorBounds Bounds(MapEditorEllipseBrushShape ellipse)
    {
        double radians = ellipse.Rotation * Math.PI / 180d;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double halfWidth = Math.Sqrt((double)ellipse.RadiusX * ellipse.RadiusX * cos * cos +
                                     (double)ellipse.RadiusY * ellipse.RadiusY * sin * sin);
        double halfHeight = Math.Sqrt((double)ellipse.RadiusX * ellipse.RadiusX * sin * sin +
                                      (double)ellipse.RadiusY * ellipse.RadiusY * cos * cos);
        return new MapEditorBounds((float)(ellipse.X - halfWidth), (float)(ellipse.Y - halfHeight),
            (float)(ellipse.X + halfWidth), (float)(ellipse.Y + halfHeight));
    }

    public static MapEditorBounds Bounds(MapEditorPolygonBrushShape polygon)
    {
        if (polygon.Vertices.IsDefaultOrEmpty)
            return default;
        return new MapEditorBounds(polygon.Vertices.Min(vertex => vertex.X),
            polygon.Vertices.Min(vertex => vertex.Y), polygon.Vertices.Max(vertex => vertex.X),
            polygon.Vertices.Max(vertex => vertex.Y));
    }

    public static Vec2 RepeatPreviewUv(Vec2 sample, MapEditorTextureProjection projection,
        int textureWidth, int textureHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);
        Vec2 origin = new(projection.Origin.X, projection.Origin.Y);
        Vec2 local = Rotate(sample - origin, -projection.Rotation) + origin;
        return new Vec2(
            (local.X - projection.Origin.X) / (textureWidth * projection.ScaleX),
            (local.Y - projection.Origin.Y) / (textureHeight * projection.ScaleY));
    }

    public static int Snap(int value, MapEditorSnap snap)
    {
        int interval = (int)snap;
        return checked((int)(Math.Round((double)value / interval,
            MidpointRounding.AwayFromZero) * interval));
    }

    public static MapEditorPoint Snap(MapEditorPoint point, MapEditorSnap snap) =>
        new(Snap(point.X, snap), Snap(point.Y, snap));

    public static MapEditorPoint SnappedDelta(MapEditorPoint start, MapEditorPoint current,
        MapEditorSnap snap) => new(
        Snap(checked(current.X - start.X), snap),
        Snap(checked(current.Y - start.Y), snap));

    public static MapEditorRectBrushShape RectBrushFromCorners(MapEditorPoint first,
        MapEditorPoint second, MapEditorSnap snap)
    {
        MapEditorPoint a = Snap(first, snap);
        MapEditorPoint b = Snap(second, snap);
        int minimum = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        return new MapEditorRectBrushShape(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Max(minimum, Math.Abs(checked(b.X - a.X))),
            Math.Max(minimum, Math.Abs(checked(b.Y - a.Y))), 0);
    }

    public static MapEditorEllipseBrushShape EllipseBrushFromCenter(MapEditorPoint center,
        MapEditorPoint edge, MapEditorSnap snap)
    {
        MapEditorPoint snappedCenter = Snap(center, snap);
        MapEditorPoint snappedEdge = Snap(edge, snap);
        int minimum = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        return new MapEditorEllipseBrushShape(snappedCenter.X, snappedCenter.Y,
            Math.Max(minimum, Math.Abs(checked(snappedEdge.X - snappedCenter.X))),
            Math.Max(minimum, Math.Abs(checked(snappedEdge.Y - snappedCenter.Y))), 0);
    }

    public static MapEditorEllipseBrushShape ResizeEllipseBrush(
        MapEditorEllipseBrushShape original, MapEditorPoint point, MapEditorSnap snap)
    {
        Vec2 local = Rotate(new Vec2(point.X - original.X, point.Y - original.Y),
            -original.Rotation);
        int minimum = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        return original with
        {
            RadiusX = Math.Max(minimum, Snap((int)MathF.Round(MathF.Abs(local.X)), snap)),
            RadiusY = Math.Max(minimum, Snap((int)MathF.Round(MathF.Abs(local.Y)), snap)),
        };
    }

    public static MapEditorEllipseBrushShape ResizeEllipseBrush(
        MapEditorEllipseBrushShape original, MapEditorPoint grabPoint,
        MapEditorPoint point, MapEditorSnap snap)
    {
        Vec2 center = new(original.X, original.Y);
        Vec2 handle = center + Rotate(new Vec2(original.RadiusX, original.RadiusY),
            original.Rotation);
        Vec2 opposite = center + Rotate(new Vec2(-original.RadiusX, -original.RadiusY),
            original.Rotation);
        Vec2 dragged = handle + new Vec2(point.X - grabPoint.X, point.Y - grabPoint.Y);
        Vec2 localSpan = Rotate(dragged - opposite, -original.Rotation);
        int radiusX = SnapDimension((int)MathF.Round(MathF.Abs(localSpan.X) / 2f), snap);
        int radiusY = SnapDimension((int)MathF.Round(MathF.Abs(localSpan.Y) / 2f), snap);
        float directionX = localSpan.X < 0 ? -1 : 1;
        float directionY = localSpan.Y < 0 ? -1 : 1;
        Vec2 snappedHandle = opposite + Rotate(new Vec2(directionX * radiusX * 2f,
            directionY * radiusY * 2f), original.Rotation);
        Vec2 resizedCenter = (opposite + snappedHandle) / 2f;
        return original with
        {
            X = (int)MathF.Round(resizedCenter.X, MidpointRounding.AwayFromZero),
            Y = (int)MathF.Round(resizedCenter.Y, MidpointRounding.AwayFromZero),
            RadiusX = radiusX,
            RadiusY = radiusY,
        };
    }

    public static MapEditorEllipseHandle PickEllipseBrushHandle(MapEditorEllipseBrushShape ellipse,
        MapEditorPoint point, float tolerance)
    {
        if (Distance(point, new MapEditorPoint(ellipse.X, ellipse.Y)) <= tolerance)
            return MapEditorEllipseHandle.MOVE;
        Vec2 handleOffset = Rotate(new Vec2(ellipse.RadiusX, ellipse.RadiusY), ellipse.Rotation);
        MapEditorPoint handle = new((int)MathF.Round(ellipse.X + handleOffset.X),
            (int)MathF.Round(ellipse.Y + handleOffset.Y));
        if (Distance(point, handle) <= tolerance)
            return MapEditorEllipseHandle.RESIZE;
        return Contains(ellipse, point) ? MapEditorEllipseHandle.MOVE : MapEditorEllipseHandle.NONE;
    }

    public static int PickPolygonVertex(MapEditorPolygonBrushShape polygon, MapEditorPoint point,
        float tolerance)
    {
        for (int index = polygon.Vertices.Length - 1; index >= 0; index--)
        {
            if (Distance(point, polygon.Vertices[index]) <= tolerance)
                return index;
        }

        return -1;
    }

    public static int PickPolygonEdge(MapEditorPolygonBrushShape polygon, MapEditorPoint point,
        float tolerance)
    {
        int best = -1;
        double bestDistance = tolerance;
        for (int index = 0; index < polygon.Vertices.Length; index++)
        {
            double distance = DistanceToSegment(point, polygon.Vertices[index],
                polygon.Vertices[(index + 1) % polygon.Vertices.Length]);
            if (distance <= bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static MapEditorPolygonBrushShape MovePolygonVertex(MapEditorPolygonBrushShape polygon,
        int vertexIndex, MapEditorPoint point, MapEditorSnap snap)
    {
        if ((uint)vertexIndex >= (uint)polygon.Vertices.Length)
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        return polygon with { Vertices = polygon.Vertices.SetItem(vertexIndex, Snap(point, snap)) };
    }

    public static MapEditorPolygonBrushShape InsertPolygonVertex(MapEditorPolygonBrushShape polygon,
        int edgeIndex, MapEditorPoint point, MapEditorSnap snap)
    {
        if ((uint)edgeIndex >= (uint)polygon.Vertices.Length)
            throw new ArgumentOutOfRangeException(nameof(edgeIndex));
        return polygon with { Vertices = polygon.Vertices.Insert(edgeIndex + 1, Snap(point, snap)) };
    }

    public static bool TryRemovePolygonVertex(MapEditorPolygonBrushShape polygon, int vertexIndex,
        out MapEditorPolygonBrushShape result, out string? error)
    {
        if ((uint)vertexIndex >= (uint)polygon.Vertices.Length)
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        result = polygon with { Vertices = polygon.Vertices.RemoveAt(vertexIndex) };
        return MapEditorBrushValidator.TryValidatePolygon(result.Vertices, out error);
    }

    public static MapEditorRectBrushShape ResizeRectBrush(MapEditorRectBrushShape original,
        MapEditorRectHandle handle, MapEditorPoint point, MapEditorSnap snap)
    {
        if (handle is MapEditorRectHandle.NONE or MapEditorRectHandle.MOVE)
            return original;

        int directionX = handle is MapEditorRectHandle.TOP_LEFT or
            MapEditorRectHandle.BOTTOM_LEFT
            ? -1
            : 1;
        int directionY = handle is MapEditorRectHandle.TOP_LEFT or
            MapEditorRectHandle.TOP_RIGHT
            ? -1
            : 1;
        Vec2 center = new(original.X + original.Width / 2f,
            original.Y + original.Height / 2f);
        Vec2 oppositeOffset = new(-directionX * original.Width / 2f,
            -directionY * original.Height / 2f);
        Vec2 opposite = center + Rotate(oppositeOffset, original.Rotation);
        Vec2 localDelta = Rotate(new Vec2(point.X - opposite.X, point.Y - opposite.Y),
            -original.Rotation);
        int minimum = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        int width = Math.Max(minimum,
            Snap((int)MathF.Round(directionX * localDelta.X), snap));
        int height = Math.Max(minimum,
            Snap((int)MathF.Round(directionY * localDelta.Y), snap));
        Vec2 dragged = opposite + Rotate(new Vec2(directionX * width,
            directionY * height), original.Rotation);
        Vec2 resizedCenter = (opposite + dragged) / 2f;
        int x = (int)MathF.Round(resizedCenter.X - width / 2f,
            MidpointRounding.AwayFromZero);
        int y = (int)MathF.Round(resizedCenter.Y - height / 2f,
            MidpointRounding.AwayFromZero);
        return new MapEditorRectBrushShape(x, y, width, height, original.Rotation);
    }

    public static MapEditorRectBrushShape ResizeRectBrush(MapEditorRectBrushShape original,
        MapEditorRectHandle handle, MapEditorPoint grabPoint, MapEditorPoint point,
        MapEditorSnap snap)
    {
        if (handle is MapEditorRectHandle.NONE or MapEditorRectHandle.MOVE)
            return original;
        int directionX = handle is MapEditorRectHandle.TOP_LEFT or
            MapEditorRectHandle.BOTTOM_LEFT
            ? -1
            : 1;
        int directionY = handle is MapEditorRectHandle.TOP_LEFT or
            MapEditorRectHandle.TOP_RIGHT
            ? -1
            : 1;
        Vec2 center = new(original.X + original.Width / 2f,
            original.Y + original.Height / 2f);
        Vec2 handlePoint = center + Rotate(new Vec2(directionX * original.Width / 2f,
            directionY * original.Height / 2f), original.Rotation);
        Vec2 opposite = center + Rotate(new Vec2(-directionX * original.Width / 2f,
            -directionY * original.Height / 2f), original.Rotation);
        Vec2 dragged = handlePoint + new Vec2(point.X - grabPoint.X, point.Y - grabPoint.Y);
        Vec2 localSpan = Rotate(dragged - opposite, -original.Rotation);
        int width = SnapDimension((int)MathF.Round(directionX * localSpan.X), snap);
        int height = SnapDimension((int)MathF.Round(directionY * localSpan.Y), snap);
        Vec2 snappedHandle = opposite + Rotate(new Vec2(directionX * width,
            directionY * height), original.Rotation);
        Vec2 resizedCenter = (opposite + snappedHandle) / 2f;
        return new MapEditorRectBrushShape(
            (int)MathF.Round(resizedCenter.X - width / 2f, MidpointRounding.AwayFromZero),
            (int)MathF.Round(resizedCenter.Y - height / 2f, MidpointRounding.AwayFromZero),
            width, height, original.Rotation);
    }

    public static MapEditorRectHandle PickRectBrushHandle(MapEditorRectBrushShape rect,
        MapEditorPoint point, float tolerance)
    {
        double centerX = rect.X + rect.Width / 2d;
        double centerY = rect.Y + rect.Height / 2d;
        (MapEditorRectHandle Handle, MapEditorPoint Point)[] corners =
        [
            (MapEditorRectHandle.TOP_LEFT, new MapEditorPoint(rect.X, rect.Y)),
            (MapEditorRectHandle.TOP_RIGHT, new MapEditorPoint(rect.X + rect.Width, rect.Y)),
            (MapEditorRectHandle.BOTTOM_RIGHT,
                new MapEditorPoint(rect.X + rect.Width, rect.Y + rect.Height)),
            (MapEditorRectHandle.BOTTOM_LEFT, new MapEditorPoint(rect.X, rect.Y + rect.Height)),
        ];
        foreach ((MapEditorRectHandle handle, MapEditorPoint corner) in corners)
        {
            MapEditorPoint rotated = Rotate(corner, centerX, centerY, rect.Rotation);
            if (Distance(point, rotated) <= tolerance)
                return handle;
        }

        return Contains(rect, point) ? MapEditorRectHandle.MOVE : MapEditorRectHandle.NONE;
    }

    public static int HitTestBrush(IReadOnlyList<MapEditorBrush> brushes, MapEditorPoint point)
    {
        for (int index = brushes.Count - 1; index >= 0; index--)
        {
            MapEditorBrush brush = brushes[index];
            if (brush.Visible && Contains(brush.Shape, point))
                return index;
        }

        return -1;
    }

    public static bool Contains(MapEditorBrushShape shape, MapEditorPoint point) => shape switch
    {
        MapEditorRectBrushShape rect => Contains(rect, point),
        MapEditorEllipseBrushShape ellipse => Contains(ellipse, point),
        MapEditorPolygonBrushShape polygon => Contains(polygon, point),
        _ => false,
    };

    private static bool Contains(MapEditorRectBrushShape rect, MapEditorPoint point)
    {
        double radians = -rect.Rotation * Math.PI / 180d;
        double centerX = rect.X + rect.Width / 2d;
        double centerY = rect.Y + rect.Height / 2d;
        double dx = point.X - centerX;
        double dy = point.Y - centerY;
        double localX = dx * Math.Cos(radians) - dy * Math.Sin(radians) + centerX;
        double localY = dx * Math.Sin(radians) + dy * Math.Cos(radians) + centerY;
        return localX >= rect.X && localX <= (long)rect.X + rect.Width &&
               localY >= rect.Y && localY <= (long)rect.Y + rect.Height;
    }

    private static bool Contains(MapEditorEllipseBrushShape ellipse, MapEditorPoint point)
    {
        Vec2 local = Rotate(new Vec2(point.X - ellipse.X, point.Y - ellipse.Y),
            -ellipse.Rotation);
        double normalizedX = local.X / ellipse.RadiusX;
        double normalizedY = local.Y / ellipse.RadiusY;
        return normalizedX * normalizedX + normalizedY * normalizedY <= 1d;
    }

    private static bool Contains(MapEditorPolygonBrushShape polygon, MapEditorPoint point)
    {
        bool inside = false;
        for (int current = 0, previous = polygon.Vertices.Length - 1;
             current < polygon.Vertices.Length;
             previous = current++)
        {
            MapEditorPoint a = polygon.Vertices[previous];
            MapEditorPoint b = polygon.Vertices[current];
            if (DistanceToSegment(point, a, b) <= 1e-7)
                return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (double)(b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    private static double Distance(MapEditorPoint first, MapEditorPoint second)
    {
        double x = (long)first.X - second.X;
        double y = (long)first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static double DistanceToSegment(MapEditorPoint point, MapEditorPoint a,
        MapEditorPoint b)
    {
        double dx = (long)b.X - a.X;
        double dy = (long)b.Y - a.Y;
        if (dx == 0 && dy == 0)
            return Distance(point, a);
        double t = Math.Clamp(((point.X - (double)a.X) * dx + (point.Y - (double)a.Y) * dy) /
                              (dx * dx + dy * dy), 0d, 1d);
        double closestX = a.X + t * dx;
        double closestY = a.Y + t * dy;
        double offsetX = point.X - closestX;
        double offsetY = point.Y - closestY;
        return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
    }

    private static MapEditorPoint Rotate(MapEditorPoint point, double centerX, double centerY,
        float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        double x = point.X - centerX;
        double y = point.Y - centerY;
        return new MapEditorPoint(
            checked((int)Math.Round(x * Math.Cos(radians) - y * Math.Sin(radians) + centerX)),
            checked((int)Math.Round(x * Math.Sin(radians) + y * Math.Cos(radians) + centerY)));
    }

    public static MapEditorBrush Move(MapEditorBrush brush, int deltaX, int deltaY)
    {
        ArgumentNullException.ThrowIfNull(brush);
        MapEditorBrushShape shape = brush.Shape switch
        {
            MapEditorRectBrushShape rect => rect with
            {
                X = checked(rect.X + deltaX),
                Y = checked(rect.Y + deltaY),
            },
            MapEditorEllipseBrushShape ellipse => ellipse with
            {
                X = checked(ellipse.X + deltaX),
                Y = checked(ellipse.Y + deltaY),
            },
            MapEditorPolygonBrushShape polygon => polygon with
            {
                Vertices = polygon.Vertices.Select(point => new MapEditorPoint(
                    checked(point.X + deltaX), checked(point.Y + deltaY))).ToImmutableArray(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(brush)),
        };
        return brush with
        {
            Shape = shape,
            Projection = brush.Projection with
            {
                Origin = new MapEditorPoint(
                    checked(brush.Projection.Origin.X + deltaX),
                    checked(brush.Projection.Origin.Y + deltaY)),
            },
        };
    }

    public static RectMapZoneShape RectFromCorners(Vec2 a, Vec2 b)
    {
        int x = (int)MathF.Min(a.X, b.X);
        int y = (int)MathF.Min(a.Y, b.Y);
        int width = Math.Max(1, (int)MathF.Abs(a.X - b.X));
        int height = Math.Max(1, (int)MathF.Abs(a.Y - b.Y));
        return new RectMapZoneShape(x, y, width, height);
    }

    public static EllipseMapZoneShape EllipseFromCenter(Vec2 center, Vec2 edge) =>
        new((int)center.X, (int)center.Y,
            Math.Max(1, (int)MathF.Abs(edge.X - center.X)),
            Math.Max(1, (int)MathF.Abs(edge.Y - center.Y)));

    public static MapZoneDef Scale(MapZoneDef zone, Vec2 anchor, Vec2 point)
        => Scale(zone, anchor, point, MapEditorSnap.NONE);

    public static MapZoneDef Scale(MapZoneDef zone, Vec2 anchor, Vec2 point,
        MapEditorSnap snap)
    {
        MapZoneShape shape = zone.Shape switch
        {
            CircleMapZoneShape circle => circle with
            {
                Radius = SnapDimension((int)MathF.Round((point - anchor).Length()), snap),
            },
            EllipseMapZoneShape ellipse => ScaleEllipse(ellipse, anchor, point, snap),
            RectMapZoneShape rect => ScaleRect(rect, anchor, point, snap),
            _ => throw new ArgumentOutOfRangeException(nameof(zone)),
        };
        return zone with { Shape = shape };
    }

    public static MapZoneDef Move(MapZoneDef zone, Vec2 delta)
    {
        int x = zone.Shape.X + (int)MathF.Round(delta.X);
        int y = zone.Shape.Y + (int)MathF.Round(delta.Y);
        MapZoneShape shape = zone.Shape switch
        {
            RectMapZoneShape rect => rect with { X = x, Y = y },
            CircleMapZoneShape circle => circle with { X = x, Y = y },
            EllipseMapZoneShape ellipse => ellipse with { X = x, Y = y },
            _ => throw new ArgumentOutOfRangeException(nameof(zone)),
        };
        return zone with { Shape = shape };
    }

    public static Vec2 Center(MapZoneShape shape) => shape switch
    {
        RectMapZoneShape rect => new Vec2(rect.X + rect.Width / 2f,
            rect.Y + rect.Height / 2f),
        CircleMapZoneShape circle => new Vec2(circle.X, circle.Y),
        EllipseMapZoneShape ellipse => new Vec2(ellipse.X, ellipse.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    public static MapZoneHandle PickHandle(MapZoneShape shape, Vec2 point,
        float tolerance, out Vec2 scaleAnchor)
    {
        Vec2 center = Center(shape);
        scaleAnchor = center;
        if ((point - center).Length() <= tolerance)
            return MapZoneHandle.MOVE;

        if (shape is CircleMapZoneShape circle)
        {
            Vec2 handle = new(circle.X + circle.Radius, circle.Y);
            return (point - handle).Length() <= tolerance
                ? MapZoneHandle.SCALE
                : MapZoneHandle.NONE;
        }

        if (shape is EllipseMapZoneShape ellipse)
        {
            Vec2 handle = Rotate(new Vec2(ellipse.RadiusX, ellipse.RadiusY),
                ellipse.Rotation) + center;
            return (point - handle).Length() <= tolerance
                ? MapZoneHandle.SCALE
                : MapZoneHandle.NONE;
        }

        RectMapZoneShape rect = (RectMapZoneShape)shape;
        Vec2[] corners =
        [
            new(rect.X, rect.Y),
            new(rect.X + rect.Width, rect.Y),
            new(rect.X + rect.Width, rect.Y + rect.Height),
            new(rect.X, rect.Y + rect.Height),
        ];
        foreach (Vec2 corner in corners)
        {
            Vec2 rotated = Rotate(corner - center, rect.Rotation) + center;
            if ((point - rotated).Length() > tolerance)
                continue;
            Vec2 opposite = new(
                corner.X == rect.X ? rect.X + rect.Width : rect.X,
                corner.Y == rect.Y ? rect.Y + rect.Height : rect.Y);
            scaleAnchor = Rotate(opposite - center, rect.Rotation) + center;
            return MapZoneHandle.SCALE;
        }

        return MapZoneHandle.NONE;
    }

    public static int HitTest(IReadOnlyList<MapZoneDef> zones, Vec2 point)
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (zones[i].Shape.Compile().Contains(point))
                return i;
        }

        return -1;
    }

    public static int HitTestSpawn(IReadOnlyList<MapSpawnPoint> spawnPoints,
        Vec2 point, float padding)
    {
        for (int i = spawnPoints.Count - 1; i >= 0; i--)
        {
            MapSpawnPoint spawn = spawnPoints[i];
            if (point.X >= spawn.X - SimConfig.PLAYER_HALF_WIDTH - padding &&
                point.X <= spawn.X + SimConfig.PLAYER_HALF_WIDTH + padding &&
                point.Y >= spawn.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 - padding &&
                point.Y <= spawn.Y + padding)
                return i;
        }

        return -1;
    }

    public static MapSpawnPoint MoveSpawn(MapSpawnPoint spawn, Vec2 grabPoint,
        Vec2 point)
    {
        int x = checked(spawn.X + (int)MathF.Round(point.X - grabPoint.X));
        int y = checked(spawn.Y + (int)MathF.Round(point.Y - grabPoint.Y));
        return spawn with { X = x, Y = y };
    }

    public static float ClampZoom(float zoom) => Math.Clamp(zoom, 0.05f, 16f);

    public static Vec2 Rotate(Vec2 point, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec2(point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos);
    }

    private static EllipseMapZoneShape ScaleEllipse(EllipseMapZoneShape ellipse,
        Vec2 center, Vec2 point, MapEditorSnap snap)
    {
        Vec2 local = Rotate(point - center, -ellipse.Rotation);
        return ellipse with
        {
            RadiusX = SnapDimension((int)MathF.Round(MathF.Abs(local.X)), snap),
            RadiusY = SnapDimension((int)MathF.Round(MathF.Abs(local.Y)), snap),
        };
    }

    private static RectMapZoneShape ScaleRect(RectMapZoneShape rect,
        Vec2 anchor, Vec2 point, MapEditorSnap snap)
    {
        Vec2 localDelta = Rotate(point - anchor, -rect.Rotation);
        int width = SnapDimension((int)MathF.Round(MathF.Abs(localDelta.X)), snap);
        int height = SnapDimension((int)MathF.Round(MathF.Abs(localDelta.Y)), snap);
        float directionX = localDelta.X < 0 ? -1 : 1;
        float directionY = localDelta.Y < 0 ? -1 : 1;
        Vec2 dragged = anchor + Rotate(new Vec2(directionX * width,
            directionY * height), rect.Rotation);
        Vec2 center = (anchor + dragged) * 0.5f;
        return rect with
        {
            X = (int)MathF.Round(center.X - width / 2f),
            Y = (int)MathF.Round(center.Y - height / 2f),
            Width = width,
            Height = height,
        };
    }

    private static int SnapDimension(int value, MapEditorSnap snap)
    {
        int minimum = snap == MapEditorSnap.NONE ? 1 : (int)snap;
        return Math.Max(minimum, Snap(Math.Abs(value), snap));
    }
}
