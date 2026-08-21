using System.Collections.Immutable;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

/// <summary>The shared authoring-space rectangle baked by every raster layer.</summary>
public readonly record struct MapEditorMapBounds(long X, long Y, long Width, long Height)
{
    public long Left => X;
    public long Top => Y;
    public long Right => checked(X + Width);
    public long Bottom => checked(Y + Height);
}

public static class MapEditorMapBoundsFitter
{
    public const int MAX_TEXTURE_DIMENSION = 8192;

    public static MapEditorMapBounds Fit(
        MapEditorBrushDocument document,
        ImmutableArray<MapEditorZone> zones,
        ImmutableArray<MapEditorSpawn> spawns,
        MapEditorMapBounds emptyFallback)
    {
        Envelope envelope = default;
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            foreach (MapEditorBrush brush in document.Layers.Get(layer).Brushes)
            {
                AddBrush(ref envelope, brush.Shape);
            }
        }
        foreach (MapEditorZone zone in zones)
        {
            AddZone(ref envelope, zone.Shape);
        }
        foreach (MapEditorSpawn spawn in spawns)
        {
            envelope.Add(spawn.Value.X, spawn.Value.Y,
                checked((long)spawn.Value.X + 1), checked((long)spawn.Value.Y + 1));
        }

        if (!envelope.HasValue)
        {
            return emptyFallback.Width > 0 && emptyFallback.Height > 0
                ? emptyFallback
                : new MapEditorMapBounds(0, 0, 1, 1);
        }

        long width = envelope.Right - envelope.Left;
        long height = envelope.Bottom - envelope.Top;
        return new MapEditorMapBounds(envelope.Left, envelope.Top, width, height);
    }

    public static MapEditorZone Translate(MapEditorZone zone, long deltaX, long deltaY) =>
        zone with { Shape = Translate(zone.Shape, deltaX, deltaY) };

    public static MapSpawnPoint Translate(MapSpawnPoint spawn, long deltaX, long deltaY) =>
        spawn with
        {
            X = checked((int)(spawn.X + deltaX)),
            Y = checked((int)(spawn.Y + deltaY)),
        };

    private static MapZoneShape Translate(MapZoneShape shape, long deltaX, long deltaY) =>
        shape switch
        {
            RectMapZoneShape rect => rect with
            {
                X = checked((int)(rect.X + deltaX)),
                Y = checked((int)(rect.Y + deltaY)),
            },
            CircleMapZoneShape circle => circle with
            {
                X = checked((int)(circle.X + deltaX)),
                Y = checked((int)(circle.Y + deltaY)),
            },
            EllipseMapZoneShape ellipse => ellipse with
            {
                X = checked((int)(ellipse.X + deltaX)),
                Y = checked((int)(ellipse.Y + deltaY)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    private static void AddBrush(ref Envelope envelope, MapEditorBrushShape shape)
    {
        switch (shape)
        {
            case MapEditorRectBrushShape rect when float.IsFinite(rect.Rotation) &&
                                                   rect.Width > 0 && rect.Height > 0:
                AddRotatedRect(ref envelope, rect.X, rect.Y, rect.Width, rect.Height,
                    rect.Rotation);
                break;
            case MapEditorEllipseBrushShape ellipse when float.IsFinite(ellipse.Rotation) &&
                                                        ellipse.RadiusX > 0 && ellipse.RadiusY > 0:
                AddEllipse(ref envelope, ellipse.X, ellipse.Y, ellipse.RadiusX,
                    ellipse.RadiusY, ellipse.Rotation);
                break;
            case MapEditorPolygonBrushShape polygon:
                foreach (MapEditorPoint point in polygon.Vertices)
                {
                    envelope.AddPoint(point.X, point.Y);
                }
                break;
        }
    }

    private static void AddZone(ref Envelope envelope, MapZoneShape shape)
    {
        switch (shape)
        {
            case RectMapZoneShape rect when float.IsFinite(rect.Rotation) &&
                                         rect.Width > 0 && rect.Height > 0:
                AddRotatedRect(ref envelope, rect.X, rect.Y, rect.Width, rect.Height,
                    rect.Rotation);
                break;
            case CircleMapZoneShape circle when circle.Radius > 0:
                envelope.Add((long)circle.X - circle.Radius, (long)circle.Y - circle.Radius,
                    (long)circle.X + circle.Radius, (long)circle.Y + circle.Radius);
                break;
            case EllipseMapZoneShape ellipse when float.IsFinite(ellipse.Rotation) &&
                                                ellipse.RadiusX > 0 && ellipse.RadiusY > 0:
                AddEllipse(ref envelope, ellipse.X, ellipse.Y, ellipse.RadiusX,
                    ellipse.RadiusY, ellipse.Rotation);
                break;
        }
    }

    private static void AddRotatedRect(ref Envelope envelope, int x, int y,
        int width, int height, float rotation)
    {
        double radians = rotation * Math.PI / 180d;
        double cosine = Math.Abs(Math.Cos(radians));
        double sine = Math.Abs(Math.Sin(radians));
        double halfWidth = width / 2d;
        double halfHeight = height / 2d;
        double extentX = cosine * halfWidth + sine * halfHeight;
        double extentY = sine * halfWidth + cosine * halfHeight;
        double centerX = x + halfWidth;
        double centerY = y + halfHeight;
        envelope.AddFloored(centerX - extentX, centerY - extentY,
            centerX + extentX, centerY + extentY);
    }

    private static void AddEllipse(ref Envelope envelope, int x, int y,
        int radiusX, int radiusY, float rotation)
    {
        double radians = rotation * Math.PI / 180d;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double extentX = Math.Sqrt(Square(radiusX * cosine) + Square(radiusY * sine));
        double extentY = Math.Sqrt(Square(radiusX * sine) + Square(radiusY * cosine));
        envelope.AddFloored(x - extentX, y - extentY, x + extentX, y + extentY);
    }

    private static double Square(double value) => value * value;

    private struct Envelope
    {
        public bool HasValue;
        public long Left;
        public long Top;
        public long Right;
        public long Bottom;

        public void AddPoint(long x, long y) => Add(x, y, x, y);

        public void AddFloored(double left, double top, double right, double bottom) => Add(
            StableFloor(left), StableFloor(top), StableCeiling(right), StableCeiling(bottom));

        private static long StableFloor(double value)
        {
            double rounded = Math.Round(value);
            return checked((long)(Math.Abs(value - rounded) < 1e-9
                ? rounded
                : Math.Floor(value)));
        }

        private static long StableCeiling(double value)
        {
            double rounded = Math.Round(value);
            return checked((long)(Math.Abs(value - rounded) < 1e-9
                ? rounded
                : Math.Ceiling(value)));
        }

        public void Add(long left, long top, long right, long bottom)
        {
            if (!HasValue)
            {
                HasValue = true;
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
                return;
            }
            Left = Math.Min(Left, left);
            Top = Math.Min(Top, top);
            Right = Math.Max(Right, right);
            Bottom = Math.Max(Bottom, bottom);
        }
    }
}
