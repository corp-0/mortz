using Mortz.Content;
using Mortz.Core.Sim.Modifiers;
using Xunit;

namespace Mortz.Tests.Content;

public class MapZoneManifestTests
{
    private const string HEADER = """
        name = "Zoned"
        suggested_players = 4
        """;

    [Fact]
    public void ZonesParseShapesTagsAndEffects()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(HEADER + """

            [[zones]]
            name = "engine_room"
            tags = ["space", "control"]
            shape = { type = "rect", x = 120, y = 40, width = 400, height = 260 }

            [[zones.effects]]
            stat = "gravity"
            op = "mul"
            value = 0.07

            [[zones.effects]]
            stat = "total_jumps"
            op = "add"
            value = 2

            [[zones]]
            name = "bubble"
            shape = { type = "circle", x = 600, y = 300, radius = 80 }
            """);

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, manifest.Zones.Length);

        MapZoneDef engineRoom = manifest.Zones[0];
        Assert.Equal("engine_room", engineRoom.Name);
        Assert.Equal(["space", "control"], engineRoom.Tags);
        Assert.Equal(400, Assert.IsType<RectMapZoneShape>(engineRoom.Shape).Width);
        Assert.Equal(
            [new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 0.07f),
             new MapZoneEffect(Stat.TOTAL_JUMPS, StatOp.ADD, 2)],
            engineRoom.Effects);

        MapZoneDef bubble = manifest.Zones[1];
        Assert.Empty(bubble.Tags);
        Assert.Empty(bubble.Effects);
        Assert.Equal(80, Assert.IsType<CircleMapZoneShape>(bubble.Shape).Radius);
    }

    [Fact]
    public void ZonesCompileToDeclarationOrderedEffectZones()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(HEADER + """

            [[zones]]
            name = "hill"
            tags = ["control"]
            shape = { type = "circle", x = 100, y = 100, radius = 50 }

            [[zones]]
            name = "space"
            shape = { type = "rect", x = 0, y = 0, width = 50, height = 50 }

            [[zones.effects]]
            stat = "gravity"
            op = "mul"
            value = 0.1
            """);

        MapZones zones = MapZoneDefs.Compile(
            Assert.IsType<MapManifest>(result.Value).Zones);
        Assert.Equal(2, zones.All.Count);
        MapZone hill = zones.All[0];
        Assert.True(hill.HasTag("control"));
        Assert.Null(hill.Effects);
        MapZone space = Assert.Single(zones.EffectZones);
        Assert.Equal("space", space.Name);
        StatsModifier effects = Assert.IsType<StatsModifier>(space.Effects);
        Assert.Equal(ModifierId.ZONE, effects.Id);
        Assert.Equal(new StatChange(Stat.GRAVITY, StatOp.MUL, 0.1f),
            Assert.Single(effects.Changes));
    }

    [Fact]
    public void RotatedEllipsesRoundTrip()
    {
        const string SHAPE =
            "shape = { type = \"ellipse\", x = 100, y = 80, radius_x = 60, " +
            "radius_y = 20, rotation = 35.5 }";
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(HEADER + $"""

            [[zones]]
            name = "oval"
            {SHAPE}
            """);

        MapManifest manifest = Assert.IsType<MapManifest>(result.Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new EllipseMapZoneShape(100, 80, 60, 20, 35.5f),
            Assert.Single(manifest.Zones).Shape);

        string written = TomlModel.Write(manifest);
        MapManifest reparsed = Assert.IsType<MapManifest>(
            TomlModel.Read<MapManifest>(written).Value);
        Assert.Equal(manifest.Zones[0].Shape, reparsed.Zones[0].Shape);
    }

    [Fact]
    public void UnknownStatNameIsRejectedWithPath()
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(HEADER + """

            [[zones]]
            name = "space"
            shape = { type = "rect", x = 0, y = 0, width = 50, height = 50 }

            [[zones.effects]]
            stat = "swagger"
            op = "mul"
            value = 0.1
            """);

        Assert.Null(result.Value);
        ContentDiagnostic error = Assert.Single(result.Diagnostics,
            d => d.Severity == ContentDiagnosticSeverity.ERROR);
        Assert.Contains("zones[0].effects[0].stat", error.Message);
    }

    [Theory]
    [InlineData("{ type = \"rect\", x = 0, y = 0, width = 50 }", "missing required key 'height'")]
    [InlineData("{ type = \"rect\", x = 0, y = 0, width = 50, height = 50, radius = 9 }",
        "unknown key 'zones[0].shape.radius'")]
    [InlineData("{ type = \"circle\", x = 0, y = 0 }", "missing required key 'radius'")]
    [InlineData("{ type = \"circle\", x = 0, y = 0, radius = 9, width = 4 }",
        "unknown key 'zones[0].shape.width'")]
    public void ShapeErrorsAndExtraFieldsAreReported(string shape, string reason)
    {
        ContentReadResult<MapManifest> result = TomlModel.Read<MapManifest>(HEADER + $"""

            [[zones]]
            name = "space"
            shape = {shape}
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Message.Contains(reason));
    }

    [Fact]
    public void ZonedManifestRoundTripsThroughWriter()
    {
        MapManifest manifest = new()
        {
            Name = "Zoned",
            SuggestedPlayers = 4,
            SpawnPoints = [new MapSpawnPoint(10, 20)],
            Zones =
            [
            new MapZoneDef
            {
                Name = "engine_room",
                Shape = new RectMapZoneShape(120, 40, 400, 260),
                Tags = ["space", "control"],
                Effects = [new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 0.07f)],
            },
            new MapZoneDef
            {
                Name = "bubble",
                Shape = new CircleMapZoneShape(600, 300, 80),
            },
            ],
        };

        string written = TomlModel.Write(manifest);
        ContentReadResult<MapManifest> reread = TomlModel.Read<MapManifest>(written);
        MapManifest parsed = Assert.IsType<MapManifest>(reread.Value);
        Assert.Empty(reread.Diagnostics);
        Assert.Equal(written, TomlModel.Write(parsed));
        Assert.Equal(manifest.Zones.Length, parsed.Zones.Length);
        for (int i = 0; i < manifest.Zones.Length; i++)
        {
            Assert.Equal(manifest.Zones[i].Name, parsed.Zones[i].Name);
            Assert.Equal(manifest.Zones[i].Shape, parsed.Zones[i].Shape);
            Assert.Equal<string>(manifest.Zones[i].Tags, parsed.Zones[i].Tags);
            Assert.Equal<MapZoneEffect>(manifest.Zones[i].Effects, parsed.Zones[i].Effects);
        }
    }

}
