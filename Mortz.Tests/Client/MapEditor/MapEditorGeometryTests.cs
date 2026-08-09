using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public class MapEditorGeometryTests
{
    [Fact]
    public void ResetViewCentersTheMapAtOneToOne()
    {
        MapEditorView view = MapEditorGeometry.ResetView(2000, 1000);

        Assert.Equal(new Vec2(1000, 500), view.CameraPosition);
        Assert.Equal(1f, view.Zoom);
    }

    [Fact]
    public void FrameViewCentersAndFitsTheWholeMap()
    {
        MapEditorView view = MapEditorGeometry.FrameView(2000, 1000, 1000, 700);

        Assert.Equal(new Vec2(1000, 500), view.CameraPosition);
        Assert.Equal(0.5f, view.Zoom);
    }

    [Fact]
    public void RectangleDragWorksInEveryDirection()
    {
        RectMapZoneShape shape = MapEditorGeometry.RectFromCorners(
            new Vec2(80, 90), new Vec2(20, 30));

        Assert.Equal(new RectMapZoneShape(20, 30, 60, 60), shape);
    }

    [Fact]
    public void OvalDragUsesIndependentRadiiFromTheCenter()
    {
        EllipseMapZoneShape shape = MapEditorGeometry.EllipseFromCenter(
            new Vec2(10, 20), new Vec2(13, 24));

        Assert.Equal(new EllipseMapZoneShape(10, 20, 3, 4), shape);
    }

    [Fact]
    public void CenterHandleMovesTheWholeShape()
    {
        MapZoneDef zone = new()
        {
            Name = "zone",
            Shape = new RectMapZoneShape(10, 20, 100, 80),
        };

        MapZoneHandle handle = MapEditorGeometry.PickHandle(zone.Shape,
            new Vec2(60, 60), 5, out _);
        MapZoneDef moved = MapEditorGeometry.Move(zone, new Vec2(15, -10));

        Assert.Equal(MapZoneHandle.MOVE, handle);
        Assert.Equal(new RectMapZoneShape(25, 10, 100, 80), moved.Shape);
    }

    [Fact]
    public void CornerHandleScalesFromTheOppositeCorner()
    {
        MapZoneDef zone = new()
        {
            Name = "zone",
            Shape = new RectMapZoneShape(10, 20, 100, 80),
        };

        MapZoneHandle handle = MapEditorGeometry.PickHandle(zone.Shape,
            new Vec2(10, 20), 5, out Vec2 anchor);
        MapZoneDef scaled = MapEditorGeometry.Scale(zone, anchor, new Vec2(30, 40));

        Assert.Equal(MapZoneHandle.SCALE, handle);
        Assert.Equal(new RectMapZoneShape(30, 40, 80, 60), scaled.Shape);
    }

    [Fact]
    public void HitTestSelectsTheTopmostOverlappingZone()
    {
        MapZoneDef[] zones =
        [
            new MapZoneDef { Name = "bottom", Shape = new RectMapZoneShape(0, 0, 100, 100) },
            new MapZoneDef { Name = "top", Shape = new CircleMapZoneShape(50, 50, 40) },
        ];

        Assert.Equal(1, MapEditorGeometry.HitTest(zones, new Vec2(50, 50)));
        Assert.Equal(0, MapEditorGeometry.HitTest(zones, new Vec2(5, 5)));
        Assert.Equal(-1, MapEditorGeometry.HitTest(zones, new Vec2(200, 200)));
    }

    [Fact]
    public void HitTestUsesRotatedEllipseGeometry()
    {
        MapZoneDef[] zones =
        [
            new MapZoneDef
            {
                Name = "oval",
                Shape = new EllipseMapZoneShape(100, 100, 60, 10, 90),
            },
        ];

        Assert.Equal(0, MapEditorGeometry.HitTest(zones, new Vec2(100, 150)));
        Assert.Equal(-1, MapEditorGeometry.HitTest(zones, new Vec2(150, 100)));
    }

    [Fact]
    public void SpawnHitTestChoosesTheTopmostNearbyPoint()
    {
        MapSpawnPoint[] spawns = [new(10, 10), new(12, 10)];

        Assert.Equal(1, MapEditorGeometry.HitTestSpawn(spawns, new Vec2(12, -5), 0));
        Assert.Equal(-1, MapEditorGeometry.HitTestSpawn(spawns, new Vec2(30, 30), 3));
    }

    [Fact]
    public void SpawnDragPreservesTheGrabOffsetAndTeam()
    {
        MapSpawnPoint spawn = new(100, 100, Team.BLUE);

        MapSpawnPoint moved = MapEditorGeometry.MoveSpawn(spawn,
            new Vec2(95, 80), new Vec2(125, 110), 500, 500);

        Assert.Equal(new MapSpawnPoint(130, 130, Team.BLUE), moved);
    }

    [Theory]
    [InlineData(0.001f, 0.05f)]
    [InlineData(1f, 1f)]
    [InlineData(100f, 16f)]
    public void ZoomSupportsAUsefulWideRange(float requested, float expected)
    {
        Assert.Equal(expected, MapEditorGeometry.ClampZoom(requested));
    }
}
