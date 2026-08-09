using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public class MapEditorDocumentTests
{
    [Fact]
    public void EditingZonesDoesNotMutateTheLoadedManifest()
    {
        MapZoneDef original = new()
        {
            Name = "old",
            Shape = new RectMapZoneShape(1, 2, 3, 4),
        };
        MapManifest manifest = new()
        {
            Name = "Map",
            SuggestedPlayers = 4,
            Zones = [original],
        };
        MapEditorDocument document = new(manifest);

        document.Replace(0, original with { Name = "new" });
        document.Add(new MapZoneDef
        {
            Name = "circle",
            Shape = new CircleMapZoneShape(10, 20, 30),
        });

        Assert.Equal("old", manifest.Zones[0].Name);
        Assert.Equal(["new", "circle"], document.BuildManifest().Zones.Select(zone => zone.Name));
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelectedZone()
    {
        MapEditorDocument document = new(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 4,
            Zones =
            [
                new MapZoneDef { Name = "one", Shape = new CircleMapZoneShape(1, 2, 3) },
                new MapZoneDef { Name = "two", Shape = new CircleMapZoneShape(4, 5, 6) },
            ],
        });

        document.RemoveAt(0);

        Assert.Equal("two", Assert.Single(document.Zones).Name);
    }

    [Fact]
    public void BuiltManifestRoundTripsGeometryTagsAndEffects()
    {
        MapEditorDocument document = new(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 4,
        });
        document.Add(new MapZoneDef
        {
            Name = "space",
            Tags = ["space", "hazard"],
            Shape = new CircleMapZoneShape(20, 30, 40),
            Effects = [new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, -1)],
        });

        string toml = TomlModel.Write(document.BuildManifest());
        MapManifest parsed = Assert.IsType<MapManifest>(TomlModel.Read<MapManifest>(toml).Value);
        MapZoneDef zone = Assert.Single(parsed.Zones);

        Assert.Equal("space", zone.Name);
        Assert.Equal(["space", "hazard"], zone.Tags);
        Assert.Equal(new CircleMapZoneShape(20, 30, 40), zone.Shape);
        Assert.Equal(new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, -1), Assert.Single(zone.Effects));
    }

    [Fact]
    public void SpawnPointEditsAreSavedWithoutMutatingTheLoadedManifest()
    {
        MapManifest manifest = new()
        {
            Name = "Map",
            SuggestedPlayers = 4,
            SpawnPoints = [new MapSpawnPoint(10, 20)],
        };
        MapEditorDocument document = new(manifest);

        document.ReplaceSpawn(0, new MapSpawnPoint(30, 40, Team.BLUE));
        document.AddSpawn(new MapSpawnPoint(50, 60));

        Assert.Equal(new MapSpawnPoint(10, 20), manifest.SpawnPoints[0]);
        Assert.Equal([new MapSpawnPoint(30, 40, Team.BLUE), new MapSpawnPoint(50, 60)],
            document.BuildManifest().SpawnPoints);
    }

    [Fact]
    public void DeleteSpawnRemovesOnlyTheSelectedPoint()
    {
        MapEditorDocument document = new(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 4,
            SpawnPoints = [new MapSpawnPoint(1, 2), new MapSpawnPoint(3, 4)],
        });

        document.RemoveSpawnAt(0);

        Assert.Equal(new MapSpawnPoint(3, 4), Assert.Single(document.SpawnPoints));
    }
}
