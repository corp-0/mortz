using Mortz.Content;
using Mortz.Core.Sim;

namespace Mortz.Client.MapEditor;

public enum MapZoneHandle
{
    NONE,
    MOVE,
    SCALE,
}

public readonly record struct MapEditorView(Vec2 CameraPosition, float Zoom);

public static class MapEditorGeometry
{
    public static MapEditorView ResetView(int mapWidth, int mapHeight) =>
        new(new Vec2(mapWidth / 2f, mapHeight / 2f), 1f);

    public static MapEditorView FrameView(int mapWidth, int mapHeight,
        float viewportWidth, float viewportHeight) =>
        new(new Vec2(mapWidth / 2f, mapHeight / 2f),
            ClampZoom(MathF.Min(viewportWidth / mapWidth, viewportHeight / mapHeight)));

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
    {
        MapZoneShape shape = zone.Shape switch
        {
            CircleMapZoneShape circle => circle with
            {
                Radius = Math.Max(1, (int)(point - anchor).Length()),
            },
            EllipseMapZoneShape ellipse => ScaleEllipse(ellipse, anchor, point),
            RectMapZoneShape rect => ScaleRect(rect, anchor, point),
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
        Vec2 point, int mapWidth, int mapHeight)
    {
        int x = Math.Clamp(spawn.X + (int)MathF.Round(point.X - grabPoint.X), 0, mapWidth);
        int y = Math.Clamp(spawn.Y + (int)MathF.Round(point.Y - grabPoint.Y), 0, mapHeight);
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
        Vec2 center, Vec2 point)
    {
        Vec2 local = Rotate(point - center, -ellipse.Rotation);
        return ellipse with
        {
            RadiusX = Math.Max(1, (int)MathF.Abs(local.X)),
            RadiusY = Math.Max(1, (int)MathF.Abs(local.Y)),
        };
    }

    private static RectMapZoneShape ScaleRect(RectMapZoneShape rect,
        Vec2 anchor, Vec2 point)
    {
        Vec2 localDelta = Rotate(point - anchor, -rect.Rotation);
        int width = Math.Max(1, (int)MathF.Abs(localDelta.X));
        int height = Math.Max(1, (int)MathF.Abs(localDelta.Y));
        Vec2 center = (anchor + point) * 0.5f;
        return rect with
        {
            X = (int)MathF.Round(center.X - width / 2f),
            Y = (int)MathF.Round(center.Y - height / 2f),
            Width = width,
            Height = height,
        };
    }
}
