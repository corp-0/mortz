using System.Collections.Immutable;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorInteractionTests
{
    [Fact]
    public void MouseMotionOnlyUpdatesThePreview()
    {
        MapEditorInteraction interaction = Open();
        int commits = 0;
        interaction.ZoneAddRequested += _ => commits++;

        interaction.BeginZoneCreation(MapEditorZoneDrag.CREATE_RECT,
            Draft(new RectMapZoneShape(10, 10, 1, 1)), new Vec2(10, 10));
        interaction.Update(new Vec2(30, 40));

        Assert.Equal(0, commits);
        Assert.Equal(new RectMapZoneShape(10, 10, 20, 30), interaction.ZonePreview!.Shape);
        Assert.Empty(interaction.Snapshot!.Zones);
    }

    [Fact]
    public void MouseReleaseEmitsOneFinalAddIntent()
    {
        MapEditorInteraction interaction = Open();
        List<MapEditorZoneDraft> commits = [];
        interaction.ZoneAddRequested += commits.Add;
        interaction.BeginZoneCreation(MapEditorZoneDrag.CREATE_CIRCLE,
            Draft(new EllipseMapZoneShape(10, 10, 1, 1)), new Vec2(10, 10));
        interaction.Update(new Vec2(20, 25));

        interaction.Commit(new Vec2(30, 35));

        MapEditorZoneDraft committed = Assert.Single(commits);
        Assert.Equal(new EllipseMapZoneShape(10, 10, 20, 25), committed.Shape);
        Assert.Null(interaction.ZonePreview);
    }

    [Fact]
    public void MouseReleaseEmitsOneFinalReplaceIntent()
    {
        MapEditorZone zone = Zone(1, "one", new CircleMapZoneShape(20, 20, 5));
        MapEditorInteraction interaction = Open(zones: [zone]);
        List<(MapEditorZoneId Id, MapEditorZoneDraft Draft)> commits = [];
        interaction.ZoneReplaceRequested += (id, draft) => commits.Add((id, draft));
        interaction.BeginZoneDrag(zone.Id, MapEditorZoneDrag.MOVE, new Vec2(20, 20));

        interaction.Commit(new Vec2(25, 30));

        (MapEditorZoneId id, MapEditorZoneDraft draft) = Assert.Single(commits);
        Assert.Equal(zone.Id, id);
        Assert.Equal(new CircleMapZoneShape(25, 30, 5), draft.Shape);
    }

    [Fact]
    public void NonzeroSpawnMovePreviewsThenCommitsOneWorkspaceRevision()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            SpawnPoints = [new MapSpawnPoint(30, 30)],
        });
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.SpawnReplaceRequested += (id, spawn) =>
        {
            MapEditorUpdate update = Assert.IsType<MapEditorUpdate>(
                workspace.ReplaceSpawn(id, spawn));
            interaction.Apply(update);
        };
        MapEditorSpawnId id = workspace.Snapshot.SpawnPoints[0].Id;

        Assert.True(interaction.BeginSpawnMove(id, new Vec2(30, 30)));
        interaction.Update(new Vec2(35, 40));

        Assert.Equal(new MapSpawnPoint(35, 40), interaction.SpawnPreview);
        Assert.Equal(new MapSpawnPoint(30, 30), workspace.Snapshot.SpawnPoints[0].Value);
        Assert.Equal(0, workspace.Snapshot.Revision);

        interaction.Commit(new Vec2(45, 50));

        Assert.Equal(new MapSpawnPoint(45, 50), workspace.Snapshot.SpawnPoints[0].Value);
        Assert.Equal(1, workspace.Snapshot.Revision);
        Assert.True(workspace.Snapshot.Dirty);
    }

    [Fact]
    public void ZoneScalePreviewsThenCommitsFinalIntentAndOneWorkspaceRevision()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
            Zones =
            [
                new MapZoneDef
                {
                    Name = "zone",
                    Shape = new RectMapZoneShape(10, 20, 20, 10),
                },
            ],
        });
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.ZoneReplaceRequested += (id, draft) =>
        {
            MapEditorUpdate update = Assert.IsType<MapEditorUpdate>(
                workspace.ReplaceZone(id, draft));
            interaction.Apply(update);
        };
        MapEditorZoneId id = workspace.Snapshot.Zones[0].Id;

        Assert.True(interaction.BeginZoneDrag(id, MapEditorZoneDrag.SCALE,
            new Vec2(30, 30), new Vec2(10, 20)));
        interaction.Update(new Vec2(40, 50));

        Assert.Equal(new RectMapZoneShape(10, 20, 30, 30),
            interaction.ZonePreview?.Shape);
        Assert.Equal(new RectMapZoneShape(10, 20, 20, 10),
            workspace.Snapshot.Zones[0].Shape);
        Assert.Equal(0, workspace.Snapshot.Revision);

        interaction.Commit(new Vec2(50, 60));

        Assert.Equal(new RectMapZoneShape(10, 20, 40, 40),
            workspace.Snapshot.Zones[0].Shape);
        Assert.Equal(1, workspace.Snapshot.Revision);
        Assert.True(workspace.Snapshot.Dirty);
    }

    [Fact]
    public void CancellationClearsPreviewAndEmitsNothing()
    {
        MapEditorInteraction interaction = Open();
        int commits = 0;
        interaction.SpawnAddRequested += _ => commits++;
        interaction.BeginSpawnCreation(new MapSpawnPoint(10, 20), new Vec2(10, 20));
        interaction.Update(new Vec2(30, 40));

        interaction.Cancel();

        Assert.Equal(0, commits);
        Assert.Null(interaction.SpawnPreview);
        Assert.False(interaction.Dragging);
    }

    [Fact]
    public void ZeroDeltaMoveEmitsNothing()
    {
        MapEditorZone zone = Zone(1, "one", new CircleMapZoneShape(20, 20, 5));
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(30, 30));
        MapEditorInteraction interaction = Open([zone], [spawn]);
        int commits = 0;
        interaction.ZoneReplaceRequested += (_, _) => commits++;
        interaction.SpawnReplaceRequested += (_, _) => commits++;

        interaction.BeginZoneDrag(zone.Id, MapEditorZoneDrag.MOVE, new Vec2(20, 20));
        interaction.Commit(new Vec2(20, 20));
        interaction.BeginSpawnMove(spawn.Id, new Vec2(30, 30));
        interaction.Commit(new Vec2(30, 30));

        Assert.Equal(0, commits);
    }

    [Fact]
    public void RemovingAnEarlierEntryPreservesSelectedIdentity()
    {
        MapEditorZone first = Zone(1, "first", new CircleMapZoneShape(10, 10, 5));
        MapEditorZone second = Zone(2, "second", new CircleMapZoneShape(30, 30, 5));
        MapEditorInteraction interaction = Open([first, second]);
        interaction.SelectZone(second.Id);
        MapEditorSnapshot removed = interaction.Snapshot! with { Zones = [second] };

        interaction.Apply(new MapEditorUpdate(removed, new MapEditorZoneRemoved(first.Id)));

        Assert.Equal(second.Id, interaction.SelectedZoneId);
    }

    [Fact]
    public void ReloadClearsSelectionAndPreview()
    {
        MapEditorZone zone = Zone(1, "one", new CircleMapZoneShape(20, 20, 5));
        MapEditorInteraction interaction = Open([zone]);
        interaction.SelectZone(zone.Id);
        interaction.BeginZoneDrag(zone.Id, MapEditorZoneDrag.MOVE, new Vec2(20, 20));
        interaction.Update(new Vec2(25, 25));

        interaction.Apply(new MapEditorUpdate(interaction.Snapshot!, new MapEditorReloaded()));

        Assert.Null(interaction.SelectedZoneId);
        Assert.Null(interaction.ZonePreview);
        Assert.False(interaction.Dragging);
    }

    [Fact]
    public void LayerSpecificUpdateOnlyDecodesTheChangedLayer()
    {
        IReadOnlyList<MapEditorLayer> layers = MapEditorCanvasLayerPlan.LayersToDecode(
            new MapEditorLayerReplaced(MapEditorLayer.SOLID));

        Assert.Equal([MapEditorLayer.SOLID], layers);
        Assert.Empty(MapEditorCanvasLayerPlan.LayersToDecode(
            new MapEditorZoneReplaced(new MapEditorZoneId(1))));
    }

    private static MapEditorInteraction Open(
        ImmutableArray<MapEditorZone> zones = default,
        ImmutableArray<MapEditorSpawn> spawns = default)
    {
        MapEditorInteraction interaction = new();
        MapEditorLayerAsset asset = new([1], 100, 80);
        MapEditorSnapshot snapshot = new("map", "Map", 2,
            zones.IsDefault ? [] : zones,
            spawns.IsDefault ? [] : spawns,
            new MapEditorLayers(asset, asset, asset), 100, 80, 0, 0, []);
        interaction.Apply(new MapEditorUpdate(snapshot, new MapEditorOpened()));
        return interaction;
    }

    private static MapEditorZone Zone(long id, string name, MapZoneShape shape) =>
        new(new MapEditorZoneId(id), name, [], shape, []);

    private static MapEditorZoneDraft Draft(MapZoneShape shape) =>
        new("zone", [], shape, []);

    private static MapEditorWorkspace Workspace(MapManifest manifest)
    {
        MapEditorLayerAsset asset = new([1], 100, 80);
        return new MapEditorWorkspace("map", manifest,
            new MapEditorLayers(asset, asset, asset));
    }
}
