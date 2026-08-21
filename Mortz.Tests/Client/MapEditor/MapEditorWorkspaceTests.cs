using System.Collections.Immutable;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorWorkspaceTests
{
    [Fact]
    public void LoadedArraysAndLayerBytesAreAdoptedImmutably()
    {
        string[] tags = ["original"];
        MapZoneEffect[] effects = [new MapZoneEffect(Stat.GRAVITY, StatOp.ADD, 2)];
        byte[] png = [1, 2, 3];
        MapEditorLayerAsset layer = new(png, 100, 80);
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "zone",
                    Tags = tags,
                    Shape = new CircleMapZoneShape(40, 40, 10),
                    Effects = effects,
                },
            ],
        }, layer);

        tags[0] = "changed";
        effects[0] = new MapZoneEffect(Stat.GRAVITY, StatOp.ADD, 5);
        png[0] = 9;
        byte[] exposedPng = workspace.Snapshot.Layers.Background.Png.ToArray();
        exposedPng[1] = 9;
        MapEditorZone zone = Assert.Single(workspace.Snapshot.Zones);
        Assert.Equal("original", Assert.Single(zone.Tags));
        Assert.Equal(2, Assert.Single(zone.Effects).Value);
        Assert.Equal(9, exposedPng[1]);
        Assert.Equal([1, 2, 3], workspace.Snapshot.Layers.Background.Png.ToArray());
    }

    [Fact]
    public void IdsAreUniqueStableAndNotPersisted()
    {
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                Zone("one", new CircleMapZoneShape(20, 20, 5)),
                Zone("two", new CircleMapZoneShape(40, 40, 5)),
            ],
        });
        MapEditorZoneId first = workspace.Snapshot.Zones[0].Id;
        MapEditorZoneId second = workspace.Snapshot.Zones[1].Id;

        MapEditorUpdate update = Assert.IsType<MapEditorUpdate>(workspace.ReplaceZone(
            first, Draft("renamed", new CircleMapZoneShape(20, 20, 5))));
        string toml = TomlModel.Write(workspace.BuildManifest());

        Assert.NotEqual(first, second);
        Assert.Equal(first, update.Snapshot.Zones[0].Id);
        Assert.DoesNotContain("id =", toml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(MapEditorZoneId), toml, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoneAndSpawnEditsPreserveOrdering()
    {
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                Zone("one", new CircleMapZoneShape(20, 20, 5)),
                Zone("two", new CircleMapZoneShape(40, 40, 5)),
            ],
            SpawnPoints = [new MapSpawnPoint(10, 10), new MapSpawnPoint(20, 20)],
        });
        MapEditorZoneId secondZone = workspace.Snapshot.Zones[1].Id;
        MapEditorSpawnId firstSpawn = workspace.Snapshot.SpawnPoints[0].Id;

        workspace.AddZone(Draft("three", new CircleMapZoneShape(60, 60, 5)));
        workspace.ReplaceZone(secondZone, Draft("second", new CircleMapZoneShape(40, 40, 5)));
        workspace.RemoveZone(workspace.Snapshot.Zones[0].Id);
        workspace.AddSpawn(new MapSpawnPoint(30, 30, Team.RED));
        workspace.ReplaceSpawn(firstSpawn, new MapSpawnPoint(11, 11, Team.BLUE));
        workspace.RemoveSpawn(workspace.Snapshot.SpawnPoints[1].Id);

        Assert.Equal(["second", "three"], workspace.Snapshot.Zones.Select(zone => zone.Name));
        Assert.Equal(
            [new MapSpawnPoint(11, 11, Team.BLUE), new MapSpawnPoint(30, 30, Team.RED)],
            workspace.Snapshot.SpawnPoints.Select(spawn => spawn.Value));
    }

    [Fact]
    public void NoOpAndMissingReplacementsDoNotCommit()
    {
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones = [Zone("one", new CircleMapZoneShape(20, 20, 5), ["tag"])],
            SpawnPoints = [new MapSpawnPoint(10, 10)],
        });
        MapEditorZone zone = workspace.Snapshot.Zones[0];
        MapEditorSpawn spawn = workspace.Snapshot.SpawnPoints[0];

        MapEditorUpdate? zoneUpdate = workspace.ReplaceZone(zone.Id,
            Draft(zone.Name, zone.Shape, [.. zone.Tags]));
        MapEditorUpdate? spawnUpdate = workspace.ReplaceSpawn(spawn.Id, spawn.Value);
        MapEditorUpdate? missingUpdate = workspace.ReplaceZone(new MapEditorZoneId(999),
            Draft("missing", new CircleMapZoneShape(20, 20, 5)));

        Assert.Null(zoneUpdate);
        Assert.Null(spawnUpdate);
        Assert.Null(missingUpdate);
        Assert.Equal(0, workspace.Snapshot.Revision);
        Assert.False(workspace.Snapshot.Dirty);
    }

    [Fact]
    public void EveryActualEditCommitsOneRevisionAndTypedChange()
    {
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });

        MapEditorUpdate zoneAdded = workspace.AddZone(
            Draft("one", new CircleMapZoneShape(20, 20, 5)));
        MapEditorUpdate spawnAdded = workspace.AddSpawn(new MapSpawnPoint(10, 10));
        MapEditorUpdate zoneRemoved = Assert.IsType<MapEditorUpdate>(
            workspace.RemoveZone(zoneAdded.Snapshot.Zones[0].Id));

        Assert.IsType<MapEditorZoneAdded>(zoneAdded.Change);
        Assert.Equal(1, zoneAdded.Snapshot.Revision);
        Assert.IsType<MapEditorSpawnAdded>(spawnAdded.Change);
        Assert.Equal(2, spawnAdded.Snapshot.Revision);
        Assert.IsType<MapEditorZoneRemoved>(zoneRemoved.Change);
        Assert.Equal(3, zoneRemoved.Snapshot.Revision);
        Assert.Equal(0, zoneRemoved.Snapshot.SavedRevision);
        Assert.True(zoneRemoved.Snapshot.Dirty);
    }

    [Fact]
    public void DuplicateZoneAndSpawnPreserveValuesOffsetAndSelectTypedAddedIdentity()
    {
        MapZoneEffect effect = new(Stat.GRAVITY, StatOp.MUL, 0.5f);
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "hazard",
                    Tags = ["damage"],
                    Shape = new CircleMapZoneShape(20, 30, 5),
                    Effects = [effect],
                }
            ],
            SpawnPoints = [new MapSpawnPoint(10, 12, Team.RED)],
        });
        MapEditorZone originalZone = workspace.Snapshot.Zones[0];
        MapEditorSpawn originalSpawn = workspace.Snapshot.SpawnPoints[0];

        MapEditorOperationResult zoneResult = workspace.DuplicateZone(originalZone.Id, 8);
        MapEditorOperationResult spawnResult = workspace.DuplicateSpawn(originalSpawn.Id, 8);

        MapEditorZoneAdded zoneAdded = Assert.IsType<MapEditorZoneAdded>(zoneResult.Update!.Change);
        MapEditorSpawnAdded spawnAdded = Assert.IsType<MapEditorSpawnAdded>(spawnResult.Update!.Change);
        MapEditorZone duplicateZone = workspace.Snapshot.Zones[1];
        MapEditorSpawn duplicateSpawn = workspace.Snapshot.SpawnPoints[1];
        Assert.Equal(zoneAdded.Id, duplicateZone.Id);
        Assert.Equal("hazard copy", duplicateZone.Name);
        Assert.Equal(originalZone.Tags, duplicateZone.Tags);
        Assert.Equal(originalZone.Effects, duplicateZone.Effects);
        Assert.Equal(new CircleMapZoneShape(28, 38, 5), duplicateZone.Shape);
        Assert.Equal(spawnAdded.Id, duplicateSpawn.Id);
        Assert.Equal(new MapSpawnPoint(18, 20, Team.RED), duplicateSpawn.Value);
        Assert.Equal(2, workspace.Snapshot.Revision);
    }

    [Fact]
    public void ValidationTracksEditsAndUsesMapDimensions()
    {
        MapEditorWorkspace workspace = CreateWorkspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });

        MapEditorUpdate outside = workspace.AddSpawn(new MapSpawnPoint(100, 10));
        MapEditorUpdate inside = Assert.IsType<MapEditorUpdate>(workspace.ReplaceSpawn(
            outside.Snapshot.SpawnPoints[0].Id, new MapSpawnPoint(99, 10)));

        Assert.Contains(outside.Snapshot.Diagnostics,
            diagnostic => diagnostic.Message.Contains("outside the 100x80 map"));
        Assert.Empty(inside.Snapshot.Diagnostics);
        Assert.False(outside.Snapshot.CanSave);
        Assert.True(inside.Snapshot.CanSave);
    }

    [Fact]
    public void ManifestConversionRoundTripsAllPersistedValues()
    {
        MapManifest manifest = new()
        {
            Name = "Full Map",
            SuggestedPlayers = 6,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "rect",
                    Tags = ["ground", "hazard"],
                    Shape = new RectMapZoneShape(10, 12, 20, 22, 15),
                    Effects = [new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 0.5f)],
                },
                Zone("circle", new CircleMapZoneShape(40, 40, 10)),
                Zone("ellipse", new EllipseMapZoneShape(60, 50, 12, 8, 30)),
            ],
            SpawnPoints =
            [
                new MapSpawnPoint(10, 20, Team.BLUE),
                new MapSpawnPoint(30, 40),
            ],
        };

        MapManifest built = CreateWorkspace(manifest, width: 100, height: 100).BuildManifest();
        MapManifest parsed = Assert.IsType<MapManifest>(
            TomlModel.Read<MapManifest>(TomlModel.Write(built)).Value);

        Assert.Equal(manifest.Name, parsed.Name);
        Assert.Equal(manifest.SuggestedPlayers, parsed.SuggestedPlayers);
        Assert.Equal(manifest.SpawnPoints, parsed.SpawnPoints);
        Assert.Equal(manifest.Zones.Select(zone => zone.Name), parsed.Zones.Select(zone => zone.Name));
        Assert.Equal(manifest.Zones.Select(zone => zone.Tags), parsed.Zones.Select(zone => zone.Tags));
        Assert.Equal(manifest.Zones.Select(zone => zone.Shape), parsed.Zones.Select(zone => zone.Shape));
        Assert.Equal(manifest.Zones.Select(zone => zone.Effects), parsed.Zones.Select(zone => zone.Effects));
    }

    private static MapEditorWorkspace CreateWorkspace(MapManifest manifest,
        MapEditorLayerAsset? layer = null, int width = 100, int height = 80)
    {
        layer ??= new MapEditorLayerAsset([1, 2, 3], width, height);
        return new MapEditorWorkspace("test-map", manifest,
            new MapEditorLayers(layer, layer, layer));
    }

    private static MapZoneDef Zone(string name, MapZoneShape shape, string[]? tags = null) =>
        new()
        {
            Name = name,
            Tags = tags ?? [],
            Shape = shape,
        };

    private static MapEditorZoneDraft Draft(string name, MapZoneShape shape,
        ImmutableArray<string> tags = default) => new(
        name,
        tags.IsDefault ? [] : tags,
        shape,
        []);
}
