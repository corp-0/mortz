using Mortz.Core.Sim.Modifiers;

namespace Mortz.Content;

/// <summary>A map zone as written in map.toml.</summary>
public sealed record MapZoneDef
{
    public required string Name { get; init; }
    public required MapZoneShape Shape { get; init; }
    /// <summary>Labels interpreted by game systems, not the map loader.</summary>
    public string[] Tags { get; init; } = [];
    public MapZoneEffect[] Effects { get; init; } = [];

    public MapZone Compile() => new(Name, Tags, Shape.Compile(),
        Effects.Length == 0
            ? null
            : new StatsModifier(ModifierId.ZONE,
                [.. Effects.Select(effect => effect.Compile())]));
}

[TomlUnion]
[TomlCase("rect", typeof(RectMapZoneShape))]
[TomlCase("circle", typeof(CircleMapZoneShape))]
public abstract record MapZoneShape(int X, int Y)
{
    public abstract ZoneShape Compile();
}

public sealed record RectMapZoneShape(int X, int Y, int Width, int Height)
    : MapZoneShape(X, Y)
{
    public override ZoneShape Compile() => ZoneShape.Rect(X, Y, Width, Height);
}

public sealed record CircleMapZoneShape(int X, int Y, int Radius)
    : MapZoneShape(X, Y)
{
    public override ZoneShape Compile() => ZoneShape.Circle(X, Y, Radius);
}

/// <summary>A stat change applied while a player is inside a zone.</summary>
public sealed record MapZoneEffect(Stat Stat, StatOp Op, float Value)
{
    public StatChange Compile() => new(Stat, Op, Value);
}

public static class MapZoneDefs
{
    public static MapZones Compile(MapZoneDef[] zones) =>
        zones.Length == 0
            ? MapZones.None
            : new MapZones([.. zones.Select(zone => zone.Compile())]);
}
