namespace Mortz.Core.Sim.Modifiers;

public enum ZoneShapeKind : byte
{
    RECT,
    CIRCLE,
    ELLIPSE,
}

/// <summary>Zone geometry in map pixels. Rectangles use a top-left X/Y;
/// circles and ellipses use a center X/Y. Rotation is in degrees.</summary>
public readonly record struct ZoneShape(ZoneShapeKind Kind, int X, int Y,
    int Width, int Height, int Radius, int RadiusY, float Rotation)
{
    public static ZoneShape Rect(int x, int y, int width, int height,
        float rotation = 0) =>
        new(ZoneShapeKind.RECT, x, y, width, height, 0, 0, rotation);

    public static ZoneShape Circle(int x, int y, int radius) =>
        new(ZoneShapeKind.CIRCLE, x, y, 0, 0, radius, radius, 0);

    public static ZoneShape Ellipse(int x, int y, int radiusX, int radiusY,
        float rotation = 0) =>
        new(ZoneShapeKind.ELLIPSE, x, y, 0, 0, radiusX, radiusY, rotation);

    public bool Contains(Vec2 point)
    {
        if (Kind == ZoneShapeKind.RECT)
        {
            Vec2 center = new(X + Width / 2f, Y + Height / 2f);
            Vec2 local = Unrotate(point - center, Rotation);
            return local.X >= -Width / 2f && local.X < Width / 2f &&
                   local.Y >= -Height / 2f && local.Y < Height / 2f;
        }
        Vec2 toCenter = Unrotate(new Vec2(point.X - X, point.Y - Y), Rotation);
        float nx = toCenter.X / Radius;
        float ny = toCenter.Y / RadiusY;
        return nx * nx + ny * ny <= 1f;
    }

    private static Vec2 Unrotate(Vec2 point, float degrees)
    {
        float radians = -degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec2(point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos);
    }
}

/// <summary>A named, tagged region of the map. Effects (if any) apply to any
/// player whose body center is inside the shape; tags are inert data that
/// modes, editors and overlays interpret, the sim never reads them.</summary>
public sealed class MapZone(
    string name,
    IReadOnlyList<string> tags,
    ZoneShape shape,
    StatsModifier? effects)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Tags { get; } = tags;
    public ZoneShape Shape { get; } = shape;
    public StatsModifier? Effects { get; } = effects;

    public bool ContainsPlayer(in PlayerState player)
    {
        return Shape.Contains(player.BodyCenter);
    }

    public bool HasTag(string tag)
    {
        foreach (string candidate in Tags)
        {
            if (string.Equals(candidate, tag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

/// <summary>One map's zones, in manifest declaration order. EffectZones is
/// the subset with stat effects; membership travels as a bit per zone, hence
/// the cap.</summary>
public sealed class MapZones
{
    public const int MAX_EFFECT_ZONES = 64;

    public static readonly MapZones None = new([]);

    public IReadOnlyList<MapZone> All { get; }
    public IReadOnlyList<MapZone> EffectZones { get; }

    public MapZones(IReadOnlyList<MapZone> zones)
    {
        All = zones;
        EffectZones = zones.Where(zone => zone.Effects != null).ToArray();
        if (EffectZones.Count > MAX_EFFECT_ZONES)
            throw new ArgumentException(
                $"more than {MAX_EFFECT_ZONES} zones with effects", nameof(zones));
    }
}
