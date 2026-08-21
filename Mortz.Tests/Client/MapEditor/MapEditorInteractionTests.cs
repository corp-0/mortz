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
        interaction.ZoneReplaceRequested += (zoneId, replacement) =>
            commits.Add((zoneId, replacement));
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
        interaction.Snap = MapEditorSnap.NONE;
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
    public void SpawnMoveIsNotClampedToTheCurrentRasterBounds()
    {
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(5, 5));
        MapEditorInteraction interaction = Open(spawns: [spawn]);
        MapSpawnPoint? replacement = null;
        interaction.SpawnReplaceRequested += (_, value) => replacement = value;

        Assert.True(interaction.BeginSpawnMove(spawn.Id, new Vec2(5, 5)));
        interaction.Commit(new Vec2(-25, 125));

        Assert.Equal(new MapSpawnPoint(-25, 125), replacement);
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
        interaction.Snap = MapEditorSnap.NONE;
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
    public void BrushZoneAndSpawnSelectionsAreMutuallyExclusive()
    {
        MapEditorBrush brush = Brush(7, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(0, 0, 20, 20, 0));
        MapEditorZone zone = Zone(1, "zone", new CircleMapZoneShape(10, 10, 5));
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(10, 10));
        MapEditorInteraction interaction = OpenSource([brush], [zone], [spawn]);

        interaction.SetEditDomain(MapEditorEditDomain.ZONES);
        interaction.SelectZone(zone.Id);
        interaction.SetEditDomain(MapEditorEditDomain.GEOMETRY);
        interaction.SelectBrush(brush.Id);

        Assert.Equal(brush.Id, interaction.SelectedBrushId);
        Assert.Null(interaction.SelectedZoneId);
        Assert.Null(interaction.SelectedSpawnId);

        interaction.SetEditDomain(MapEditorEditDomain.SPAWNS);
        interaction.SelectSpawn(spawn.Id);

        Assert.Null(interaction.SelectedBrushId);
        Assert.Equal(spawn.Id, interaction.SelectedSpawnId);
    }

    [Fact]
    public void SelectedLayerRejectsBrushesFromOtherLayers()
    {
        MapEditorBrush background = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(0, 0, 20, 20, 0));
        MapEditorBrush solid = Brush(2, MapEditorLayer.SOLID,
            new MapEditorRectBrushShape(0, 0, 20, 20, 0));
        MapEditorInteraction interaction = OpenSource([background, solid]);

        interaction.SelectBrush(solid.Id);
        Assert.Null(interaction.SelectedBrushId);

        interaction.SelectLayer(MapEditorLayer.SOLID);
        interaction.SelectBrush(solid.Id);

        Assert.Equal(solid.Id, interaction.SelectedBrushId);
    }

    [Fact]
    public void BrushDragPreviewsAndCommitsExactlyOnce()
    {
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(5, 7, 19, 13, 0));
        MapEditorInteraction interaction = OpenSource([brush]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        List<MapEditorBrushDraft> commits = [];
        interaction.BrushReplaceRequested += (_, draft) => commits.Add(draft);

        Assert.True(interaction.BeginBrushDrag(brush.Id, MapEditorRectHandle.MOVE,
            new MapEditorPoint(11, 12)));
        interaction.Update(new Vec2(22, 25));
        interaction.Update(new Vec2(28, 27));

        Assert.Empty(commits);
        Assert.Equal(new MapEditorRectBrushShape(21, 23, 19, 13, 0),
            interaction.BrushPreview?.Shape);

        interaction.Commit(new Vec2(28, 27));

        Assert.Single(commits);
        Assert.Equal(new MapEditorRectBrushShape(21, 23, 19, 13, 0), commits[0].Shape);
    }

    [Fact]
    public void EscapeCancellationRestoresOriginalBrushWithoutIntent()
    {
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(8, 8, 16, 16, 0));
        MapEditorInteraction interaction = OpenSource([brush]);
        int commits = 0;
        interaction.BrushReplaceRequested += (_, _) => commits++;
        interaction.BeginBrushDrag(brush.Id, MapEditorRectHandle.BOTTOM_RIGHT,
            new MapEditorPoint(24, 24));
        interaction.Update(new Vec2(48, 48));

        interaction.Cancel();

        Assert.Equal(0, commits);
        Assert.Null(interaction.BrushPreview);
        Assert.Equal(new MapEditorRectBrushShape(8, 8, 16, 16, 0), brush.Shape);
    }

    [Fact]
    public void CompleteBrushDragCreatesOneWorkspaceHistoryEntry()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });
        workspace.InitializeBrushSource();
        MapEditorBrushAdded added = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            new MapEditorBrushDraft("brush", MapEditorLayer.BACKGROUND,
                new MapEditorRectBrushShape(8, 8, 16, 16, 0),
                new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
                new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                    new MapEditorPoint(8, 8), 1, 1, 0))).Update?.Change);
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.Snap = MapEditorSnap.PIXELS_32;
        interaction.BrushReplaceRequested += (id, draft) =>
        {
            MapEditorOperationResult result = workspace.ReplaceBrush(id, draft);
            interaction.Apply(Assert.IsType<MapEditorUpdate>(result.Update));
        };
        long revision = workspace.Snapshot.Revision;

        interaction.BeginBrushDrag(added.Id, MapEditorRectHandle.MOVE,
            new MapEditorPoint(10, 10));
        interaction.Update(new Vec2(20, 20));
        interaction.Update(new Vec2(30, 30));
        interaction.Commit(new Vec2(34, 34));

        Assert.Equal(revision + 1, workspace.Snapshot.Revision);
        Assert.Equal(new MapEditorRectBrushShape(40, 40, 16, 16, 0),
            Assert.Single(workspace.Snapshot.BrushDocument!.Layers.Background.Brushes).Shape);

        workspace.Undo();

        Assert.Equal(new MapEditorRectBrushShape(8, 8, 16, 16, 0),
            Assert.Single(workspace.Snapshot.BrushDocument!.Layers.Background.Brushes).Shape);
    }

    [Fact]
    public void CommittedBrushMoveCanImmediatelyStartAnotherMove()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });
        workspace.InitializeBrushSource();
        MapEditorBrushAdded added = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            new MapEditorBrushDraft("brush", MapEditorLayer.BACKGROUND,
                new MapEditorRectBrushShape(0, 0, 16, 16, 0),
                new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
                new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                    new MapEditorPoint(0, 0), 1, 1, 0))).Update?.Change);
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.BrushReplaceRequested += (id, draft) =>
        {
            MapEditorUpdate update = Assert.IsType<MapEditorUpdate>(
                workspace.ReplaceBrush(id, draft).Update);
            interaction.Apply(update);
        };

        Assert.True(interaction.BeginBrushMove(added.Id, new MapEditorPoint(4, 4)));
        interaction.Commit(new Vec2(12, 12));
        Assert.False(interaction.Dragging);
        Assert.True(interaction.BeginBrushMove(added.Id, new MapEditorPoint(12, 12)));
        interaction.Commit(new Vec2(20, 20));

        Assert.Equal(new MapEditorRectBrushShape(16, 16, 16, 16, 0),
            Assert.Single(workspace.Snapshot.BrushDocument!.Layers.Background.Brushes).Shape);
        Assert.False(interaction.Dragging);
    }

    [Fact]
    public void RotatedRectangleResizeCancelCommitAndUndoPreserveProjectionAndHistory()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });
        workspace.InitializeBrushSource();
        MapEditorTextureProjection projection = new(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(7, -9), 2, 0.5f, 30);
        MapEditorRectBrushShape original = new(8, 8, 32, 16, 90);
        MapEditorBrushAdded added = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            new MapEditorBrushDraft("rotated", MapEditorLayer.BACKGROUND, original,
                new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")), projection)).Update?.Change);
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.Snap = MapEditorSnap.PIXELS_8;
        interaction.BrushReplaceRequested += (id, draft) =>
        {
            MapEditorOperationResult result = workspace.ReplaceBrush(id, draft);
            interaction.Apply(Assert.IsType<MapEditorUpdate>(result.Update));
        };
        long revision = workspace.Snapshot.Revision;
        MapEditorPoint target = new(0, 56);

        Assert.True(interaction.BeginBrushDrag(added.Id, MapEditorRectHandle.BOTTOM_RIGHT,
            new MapEditorPoint(16, 40)));
        interaction.Update(new Vec2(target.X, target.Y));
        interaction.Cancel();
        Assert.Equal(revision, workspace.Snapshot.Revision);

        interaction.BeginBrushDrag(added.Id, MapEditorRectHandle.BOTTOM_RIGHT,
            new MapEditorPoint(16, 40));
        interaction.Commit(new Vec2(target.X, target.Y));

        Assert.Equal(revision + 1, workspace.Snapshot.Revision);
        MapEditorBrush resized = Assert.Single(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes);
        Assert.Equal(new MapEditorRectBrushShape(-8, 8, 48, 32, 90), resized.Shape);
        Assert.Equal(projection, resized.Projection);

        workspace.Undo();

        MapEditorBrush restored = Assert.Single(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes);
        Assert.Equal(original, restored.Shape);
        Assert.Equal(projection, restored.Projection);
    }

    [Fact]
    public void EllipseCreationMoveResizeAndCancelUseOneIntentPerGesture()
    {
        MapEditorInteraction interaction = OpenSource([]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        List<MapEditorBrushDraft> additions = [];
        interaction.BrushAddRequested += additions.Add;
        MapEditorPoint center = new(16, 16);
        interaction.BeginBrushCreation(new MapEditorBrushDraft("ellipse",
            MapEditorLayer.BACKGROUND, new MapEditorEllipseBrushShape(16, 16, 8, 8, 0),
            new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT, center, 1, 1, 0)), center);

        interaction.Update(new Vec2(39, 27));
        interaction.Commit(new Vec2(39, 27));

        MapEditorBrushDraft ellipse = Assert.Single(additions);
        Assert.Equal(new MapEditorEllipseBrushShape(16, 16, 24, 8, 0), ellipse.Shape);
    }

    [Fact]
    public void PolygonCreationSupportsBackspaceRejectsInvalidAndClosesOnce()
    {
        MapEditorInteraction interaction = OpenSource([]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        List<MapEditorBrushDraft> additions = [];
        interaction.BrushAddRequested += additions.Add;
        interaction.BeginPolygonCreation(new MapEditorBrushDraft("polygon",
            MapEditorLayer.BACKGROUND, new MapEditorPolygonBrushShape([]),
            new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                new MapEditorPoint(0, 0), 1, 1, 0)));
        interaction.AppendPolygonVertex(new MapEditorPoint(1, 1));
        interaction.AppendPolygonVertex(new MapEditorPoint(17, 1));
        interaction.AppendPolygonVertex(new MapEditorPoint(17, 17));
        interaction.AppendPolygonVertex(new MapEditorPoint(1, 17));
        Assert.True(interaction.RemoveLastPolygonVertex());

        Assert.True(interaction.TryCommitPolygonCreation());
        MapEditorPolygonBrushShape polygon = Assert.IsType<MapEditorPolygonBrushShape>(
            Assert.Single(additions).Shape);
        Assert.Equal([
            new MapEditorPoint(0, 0), new MapEditorPoint(16, 0),
            new MapEditorPoint(16, 16)
        ], polygon.Vertices);

        interaction.BeginPolygonCreation(new MapEditorBrushDraft("bow",
            MapEditorLayer.BACKGROUND, new MapEditorPolygonBrushShape([]),
            new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                new MapEditorPoint(0, 0), 1, 1, 0)));
        foreach (MapEditorPoint vertex in new[]
                 {
                     new MapEditorPoint(0, 0),
                     new MapEditorPoint(16, 16), new MapEditorPoint(0, 16),
                     new MapEditorPoint(16, 0)
                 })
        {
            interaction.AppendPolygonVertex(vertex);
        }

        Assert.False(interaction.TryCommitPolygonCreation());
        Assert.Contains("self-intersect", interaction.BrushDiagnostic);
        Assert.Single(additions);
    }

    [Fact]
    public void PolygonVertexDragSupportsSnapCancelAndCommit()
    {
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorPolygonBrushShape([
                new MapEditorPoint(0, 0),
                new MapEditorPoint(32, 0), new MapEditorPoint(0, 32)
            ]));
        MapEditorInteraction interaction = OpenSource([brush]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        List<MapEditorBrushDraft> replacements = [];
        interaction.BrushReplaceRequested += (_, draft) => replacements.Add(draft);

        Assert.True(interaction.BeginPolygonVertexDrag(brush.Id, 1,
            new MapEditorPoint(32, 0)));
        interaction.Update(new Vec2(29, 11));
        interaction.Cancel();
        Assert.Empty(replacements);

        interaction.BeginPolygonVertexDrag(brush.Id, 1, new MapEditorPoint(32, 0));
        interaction.Commit(new Vec2(29, 11));
        MapEditorPolygonBrushShape changed = Assert.IsType<MapEditorPolygonBrushShape>(
            Assert.Single(replacements).Shape);
        Assert.Equal(new MapEditorPoint(32, 8), changed.Vertices[1]);
        Assert.Equal(brush.Projection, replacements[0].Projection);
    }

    [Fact]
    public void PolygonEdgeInsertionAndValidRemovalEachEmitOneIntent()
    {
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorPolygonBrushShape([
                new MapEditorPoint(0, 0),
                new MapEditorPoint(32, 0), new MapEditorPoint(32, 32),
                new MapEditorPoint(0, 32)
            ]));
        MapEditorInteraction interaction = OpenSource([brush]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        List<MapEditorBrushDraft> replacements = [];
        interaction.BrushReplaceRequested += (_, draft) => replacements.Add(draft);

        Assert.True(interaction.BeginPolygonEdgeInsertion(brush.Id, 0,
            new MapEditorPoint(13, 2)));
        interaction.Commit(new Vec2(13, 2));

        MapEditorPolygonBrushShape inserted = Assert.IsType<MapEditorPolygonBrushShape>(
            Assert.Single(replacements).Shape);
        Assert.Equal(new MapEditorPoint(16, 0), inserted.Vertices[1]);

        interaction.Apply(new MapEditorUpdate(interaction.Snapshot! with
        {
            BrushDocument = interaction.Snapshot.BrushDocument! with
            {
                Layers = interaction.Snapshot.BrushDocument.Layers with
                {
                    Background = interaction.Snapshot.BrushDocument.Layers.Background with
                    {
                        Brushes = [brush with { Shape = inserted }],
                    },
                },
            },
        }, new MapEditorBrushReplaced(brush.Id)));
        Assert.True(interaction.RemovePolygonVertex(brush.Id, 1));
        Assert.Equal(2, replacements.Count);
        Assert.Equal(((MapEditorPolygonBrushShape)brush.Shape).Vertices,
            ((MapEditorPolygonBrushShape)replacements[1].Shape).Vertices);
    }

    [Fact]
    public void CommittedVertexGestureIsOneUndoableWorkspaceHistoryEntry()
    {
        MapEditorWorkspace workspace = Workspace(new MapManifest
        {
            Name = "Map",
            SuggestedPlayers = 2,
        });
        workspace.InitializeBrushSource();
        MapEditorPolygonBrushShape original = new([
            new MapEditorPoint(0, 0),
            new MapEditorPoint(32, 0), new MapEditorPoint(0, 32)
        ]);
        MapEditorBrushAdded added = Assert.IsType<MapEditorBrushAdded>(workspace.AddBrush(
            new MapEditorBrushDraft("polygon", MapEditorLayer.BACKGROUND, original,
                new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
                new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                    new MapEditorPoint(0, 0), 1, 1, 0))).Update?.Change);
        MapEditorInteraction interaction = new();
        interaction.Apply(new MapEditorUpdate(workspace.Snapshot, new MapEditorOpened()));
        interaction.Snap = MapEditorSnap.PIXELS_32;
        interaction.BrushReplaceRequested += (id, draft) =>
            interaction.Apply(Assert.IsType<MapEditorUpdate>(workspace.ReplaceBrush(id, draft).Update));
        long revision = workspace.Snapshot.Revision;

        interaction.BeginPolygonVertexDrag(added.Id, 1, new MapEditorPoint(32, 0));
        interaction.Update(new Vec2(40, 8));
        interaction.Commit(new Vec2(48, 8));

        Assert.Equal(revision + 1, workspace.Snapshot.Revision);
        MapEditorPolygonBrushShape changed = Assert.IsType<MapEditorPolygonBrushShape>(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes[0].Shape);
        Assert.Equal(new MapEditorPoint(64, 0), changed.Vertices[1]);

        workspace.Undo();
        Assert.Equal(original.Vertices, Assert.IsType<MapEditorPolygonBrushShape>(
            workspace.Snapshot.BrushDocument!.Layers.Background.Brushes[0].Shape).Vertices);
    }

    [Fact]
    public void OpenChoosesDomainFromSourceAndReloadClearsGestureAndSelection()
    {
        MapEditorZone zone = Zone(1, "zone", new CircleMapZoneShape(8, 8, 4));
        MapEditorBrush brush = Brush(2, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(0, 0, 16, 16, 0));
        MapEditorInteraction raster = Open([zone]);
        MapEditorInteraction source = OpenSource([brush], [zone]);

        Assert.Equal(MapEditorEditDomain.ZONES, raster.EditDomain);
        Assert.Equal(MapEditorEditDomain.GEOMETRY, source.EditDomain);
        source.SelectBrush(brush.Id);
        Assert.True(source.BeginBrushMove(brush.Id, new MapEditorPoint(4, 4)));

        source.Apply(new MapEditorUpdate(source.Snapshot!, new MapEditorReloaded()));

        Assert.Equal(MapEditorEditDomain.GEOMETRY, source.EditDomain);
        Assert.Null(source.SelectedBrushId);
        Assert.False(source.Dragging);
    }

    [Fact]
    public void DomainSwitchCancelsGestureAndClearsIncompatibleSelectionWithoutEditIntent()
    {
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(3, 5, 17, 19, 0));
        MapEditorInteraction interaction = OpenSource([brush]);
        int replacements = 0;
        interaction.BrushReplaceRequested += (_, _) => replacements++;
        interaction.SelectBrush(brush.Id);
        interaction.BeginBrushMove(brush.Id, new MapEditorPoint(5, 7));
        interaction.Update(new Vec2(21, 23));

        interaction.SetEditDomain(MapEditorEditDomain.ZONES);

        Assert.Equal(0, replacements);
        Assert.Null(interaction.SelectedBrushId);
        Assert.Null(interaction.BrushPreview);
        Assert.False(interaction.Dragging);
    }

    [Fact]
    public void UniversalSnapPreservesOffGridOriginsForZoneAndSpawnMovement()
    {
        MapEditorZone zone = Zone(1, "zone", new CircleMapZoneShape(3, -5, 4));
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(-3, 5));
        MapEditorInteraction interaction = Open([zone], [spawn]);
        interaction.Snap = MapEditorSnap.PIXELS_8;
        MapEditorZoneDraft? movedZone = null;
        MapSpawnPoint? movedSpawn = null;
        interaction.ZoneReplaceRequested += (_, draft) => movedZone = draft;
        interaction.SpawnReplaceRequested += (_, value) => movedSpawn = value;

        interaction.BeginZoneDrag(zone.Id, MapEditorZoneDrag.MOVE, new Vec2(3, -5));
        interaction.Commit(new Vec2(-1, -9));
        interaction.SetEditDomain(MapEditorEditDomain.SPAWNS);
        interaction.BeginSpawnMove(spawn.Id, new Vec2(-3, 5));
        interaction.Commit(new Vec2(-7, 9));

        Assert.Equal(new CircleMapZoneShape(-5, -13, 4), movedZone?.Shape);
        Assert.Equal(new MapSpawnPoint(-11, 13), movedSpawn);
    }

    [Theory]
    [InlineData(-4, -8)]
    [InlineData(4, 8)]
    public void UniversalSnapRoundsNegativeAndPositiveMidpointsAwayFromZero(
        int value, int expected)
    {
        Assert.Equal(expected, MapEditorGeometry.Snap(value, MapEditorSnap.PIXELS_8));
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
        interaction.Snap = MapEditorSnap.NONE;
        return interaction;
    }

    private static MapEditorInteraction OpenSource(ImmutableArray<MapEditorBrush> brushes,
        ImmutableArray<MapEditorZone> zones = default,
        ImmutableArray<MapEditorSpawn> spawns = default)
    {
        MapEditorInteraction interaction = new();
        MapEditorLayerAsset asset = new([1], 100, 80);
        MapEditorLayerSource background = new(
            brushes.Where(brush => brush.Layer == MapEditorLayer.BACKGROUND).ToImmutableArray(),
            asset, false);
        MapEditorLayerSource solid = new(
            brushes.Where(brush => brush.Layer == MapEditorLayer.SOLID).ToImmutableArray(),
            asset, false);
        MapEditorLayerSource destructible = new(
            brushes.Where(brush => brush.Layer == MapEditorLayer.DESTRUCTIBLE).ToImmutableArray(),
            asset, false);
        MapEditorBrushDocument document = new(MapEditorBrushDocument.CURRENT_VERSION,
            brushes.IsEmpty ? 1 : brushes.Max(brush => brush.Id.Value) + 1,
            new MapEditorLayerSources(background, solid, destructible));
        MapEditorSnapshot snapshot = new("map", "Map", 2,
            zones.IsDefault ? [] : zones, spawns.IsDefault ? [] : spawns,
            new MapEditorLayers(asset, asset, asset), 100, 80, 0, 0, [],
            MapEditorRasterSourceStatus.BRUSH_SOURCE, document);
        interaction.Apply(new MapEditorUpdate(snapshot, new MapEditorOpened()));
        return interaction;
    }

    private static MapEditorZone Zone(long id, string name, MapZoneShape shape) =>
        new(new MapEditorZoneId(id), name, [], shape, []);

    private static MapEditorZoneDraft Draft(MapZoneShape shape) =>
        new("zone", [], shape, []);

    private static MapEditorBrush Brush(long id, MapEditorLayer layer,
        MapEditorBrushShape shape) => new(new MapEditorBrushId(id), $"brush-{id}", layer, shape,
        new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(0, 0), 1, 1, 0), true);

    private static MapEditorWorkspace Workspace(MapManifest manifest)
    {
        MapEditorLayerAsset asset = new([1], 100, 80);
        return new MapEditorWorkspace("map", manifest,
            new MapEditorLayers(asset, asset, asset));
    }
}
