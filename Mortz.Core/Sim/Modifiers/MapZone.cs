namespace Mortz.Core.Sim.Modifiers;

public enum ZoneShapeKind : byte
{
    RECT,
    CIRCLE,
}

/// <summary>Zone geometry in map pixels. RECT anchors X,Y at the top-left
/// corner; CIRCLE centers on X,Y. Integer authored coordinates, float
/// containment: both sides run the same math on the same values, so
/// membership is deterministic.</summary>
public readonly record struct ZoneShape(ZoneShapeKind Kind, int X, int Y,
    int Width, int Height, int Radius)
{
    public static ZoneShape Rect(int x, int y, int width, int height) =>
        new(ZoneShapeKind.RECT, x, y, width, height, 0);

    public static ZoneShape Circle(int x, int y, int radius) =>
        new(ZoneShapeKind.CIRCLE, x, y, 0, 0, radius);

    public bool Contains(Vec2 point)
    {
        if (Kind == ZoneShapeKind.RECT)
        {
            return point.X >= X && point.X < X + Width &&
                   point.Y >= Y && point.Y < Y + Height;
        }
        Vec2 toCenter = new(point.X - X, point.Y - Y);
        return toCenter.LengthSquared() <= (float)Radius * Radius;
    }
}

/// <summary>A named, tagged region of the map. Effects (if any) apply to any
/// player whose body center is inside the shape; tags are inert data that
/// modes, editors and overlays interpret, the sim never reads them.</summary>
public sealed class MapZone
{
    public string Name { get; }
    public IReadOnlyList<string> Tags { get; }
    public ZoneShape Shape { get; }
    public StatsModifier? Effects { get; }

    public MapZone(string name, IReadOnlyList<string> tags, ZoneShape shape,
        StatsModifier? effects)
    {
        Name = name;
        Tags = tags;
        Shape = shape;
        Effects = effects;
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
