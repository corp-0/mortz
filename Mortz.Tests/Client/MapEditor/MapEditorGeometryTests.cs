using Mortz.Client.MapEditor;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public class MapEditorGeometryTests
{
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
    public void SpawnDragPreservesTheGrabOffsetAndTeamWithoutWorldBounds()
    {
        MapSpawnPoint spawn = new(100, 100, Team.BLUE);

        MapSpawnPoint moved = MapEditorGeometry.MoveSpawn(spawn,
            new Vec2(95, 80), new Vec2(-125, -110));

        Assert.Equal(new MapSpawnPoint(-120, -90, Team.BLUE), moved);
    }

    [Theory]
    [InlineData(0.001f, 0.05f)]
    [InlineData(1f, 1f)]
    [InlineData(100f, 16f)]
    public void ZoomSupportsAUsefulWideRange(float requested, float expected)
    {
        Assert.Equal(expected, MapEditorGeometry.ClampZoom(requested));
    }

    [Theory]
    [InlineData(13, MapEditorSnap.PIXELS_8, 16)]
    [InlineData(-13, MapEditorSnap.PIXELS_8, -16)]
    [InlineData(-3, MapEditorSnap.PIXELS_8, 0)]
    [InlineData(13, MapEditorSnap.NONE, 13)]
    public void SharedSnapHandlesPositiveAndNegativeCoordinates(int value,
        MapEditorSnap snap, int expected)
    {
        Assert.Equal(expected, MapEditorGeometry.Snap(value, snap));
    }

    [Theory]
    [InlineData(1, MapEditorSnap.PIXELS_32, 0)]
    [InlineData(31, MapEditorSnap.PIXELS_32, 0)]
    [InlineData(32, MapEditorSnap.PIXELS_32, 32)]
    [InlineData(-1, MapEditorSnap.PIXELS_32, -32)]
    [InlineData(-32, MapEditorSnap.PIXELS_32, -32)]
    [InlineData(13, MapEditorSnap.NONE, 13)]
    public void StampSnapUsesTheCellContainingTheCursor(int value,
        MapEditorSnap snap, int expected)
    {
        MapEditorPoint snapped = MapEditorStampGeometry.SnapToCell(
            new MapEditorPoint(value, value), snap);

        Assert.Equal(new MapEditorPoint(expected, expected), snapped);
    }

    [Fact]
    public void StampStrokeFillsEveryCrossedSnapCell()
    {
        MapEditorPoint[] cells = MapEditorStampGeometry.CellsAlongStroke(
            new MapEditorPoint(1, 1), new MapEditorPoint(127, 63),
            MapEditorSnap.PIXELS_32).ToArray();

        Assert.Equal([
            new MapEditorPoint(0, 0),
            new MapEditorPoint(32, 0),
            new MapEditorPoint(64, 32),
            new MapEditorPoint(96, 32),
        ], cells);
    }

    [Fact]
    public void SnappedMovementUsesDeltaWithoutDeformingRectangle()
    {
        MapEditorPoint delta = MapEditorGeometry.SnappedDelta(
            new MapEditorPoint(13, 18), new MapEditorPoint(35, 7), MapEditorSnap.PIXELS_16);
        MapEditorBrush brush = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(3, 5, 27, 19, 0));

        MapEditorBrush moved = MapEditorGeometry.Move(brush, delta.X, delta.Y);

        Assert.Equal(new MapEditorPoint(16, -16), delta);
        Assert.Equal(new MapEditorRectBrushShape(19, -11, 27, 19, 0), moved.Shape);
    }

    [Fact]
    public void BrushHitTestOnlyConsidersVisibleBrushesAndReturnsTopmost()
    {
        MapEditorBrush[] brushes =
        [
            Brush(1, MapEditorLayer.BACKGROUND, new MapEditorRectBrushShape(0, 0, 50, 50, 0)),
            Brush(2, MapEditorLayer.BACKGROUND,
                new MapEditorRectBrushShape(10, 10, 50, 50, 0)),
            Brush(3, MapEditorLayer.BACKGROUND,
                new MapEditorRectBrushShape(10, 10, 50, 50, 0)) with { Visible = false },
        ];

        Assert.Equal(1, MapEditorGeometry.HitTestBrush(brushes, new MapEditorPoint(20, 20)));
        Assert.Equal(0, MapEditorGeometry.HitTestBrush(brushes, new MapEditorPoint(5, 5)));
        Assert.Equal(-1, MapEditorGeometry.HitTestBrush(brushes, new MapEditorPoint(70, 70)));
    }

    [Fact]
    public void RectangleResizeKeepsOppositeCornerAndObeysSnap()
    {
        MapEditorRectBrushShape resized = MapEditorGeometry.ResizeRectBrush(
            new MapEditorRectBrushShape(16, 16, 32, 32, 0),
            MapEditorRectHandle.TOP_LEFT, new MapEditorPoint(27, 3), MapEditorSnap.PIXELS_8);

        Assert.Equal(new MapEditorRectBrushShape(24, 0, 24, 48, 0), resized);
    }

    [Theory]
    [InlineData(MapEditorRectHandle.TOP_LEFT, 0)]
    [InlineData(MapEditorRectHandle.TOP_RIGHT, 0)]
    [InlineData(MapEditorRectHandle.BOTTOM_RIGHT, 0)]
    [InlineData(MapEditorRectHandle.BOTTOM_LEFT, 0)]
    [InlineData(MapEditorRectHandle.TOP_LEFT, 45)]
    [InlineData(MapEditorRectHandle.TOP_RIGHT, 45)]
    [InlineData(MapEditorRectHandle.BOTTOM_RIGHT, 45)]
    [InlineData(MapEditorRectHandle.BOTTOM_LEFT, 45)]
    [InlineData(MapEditorRectHandle.TOP_LEFT, 90)]
    [InlineData(MapEditorRectHandle.TOP_RIGHT, 90)]
    [InlineData(MapEditorRectHandle.BOTTOM_RIGHT, 90)]
    [InlineData(MapEditorRectHandle.BOTTOM_LEFT, 90)]
    public void RotatedRectangleResizeUsesLocalAxesAndKeepsTheOppositeHandle(
        MapEditorRectHandle handle, float rotation)
    {
        MapEditorRectBrushShape original = new(40, 50, 32, 16, rotation);
        (int directionX, int directionY) = HandleDirections(handle);
        Vec2 opposite = RectHandleWorld(original, -directionX, -directionY);
        Vec2 target = opposite + Rotate(new Vec2(directionX * 48, directionY * 24), rotation);

        MapEditorRectBrushShape resized = MapEditorGeometry.ResizeRectBrush(original, handle,
            new MapEditorPoint((int)MathF.Round(target.X), (int)MathF.Round(target.Y)),
            MapEditorSnap.NONE);

        Assert.Equal(48, resized.Width);
        Assert.Equal(24, resized.Height);
        Assert.Equal(rotation, resized.Rotation);
        Vec2 anchored = RectHandleWorld(resized, -directionX, -directionY);
        Assert.InRange((anchored - opposite).Length(), 0, 1.5f);
    }

    [Fact]
    public void RotatedRectangleResizeSupportsNegativeWorldCoordinatesWithoutCollapse()
    {
        MapEditorRectBrushShape original = new(-40, -30, 32, 16, 90);
        Vec2 opposite = RectHandleWorld(original, -1, -1);
        Vec2 target = opposite + Rotate(new Vec2(64, 40), 90);

        MapEditorRectBrushShape resized = MapEditorGeometry.ResizeRectBrush(original,
            MapEditorRectHandle.BOTTOM_RIGHT,
            new MapEditorPoint((int)target.X, (int)target.Y), MapEditorSnap.PIXELS_8);

        Assert.Equal(64, resized.Width);
        Assert.Equal(40, resized.Height);
        Assert.True(resized.X < 0);
        Assert.True(resized.Y < 0);
    }

    [Fact]
    public void RotatedRectangleResizePastTheAnchorClampsPositiveInLocalSpace()
    {
        MapEditorRectBrushShape original = new(20, 20, 32, 16, 90);
        Vec2 opposite = RectHandleWorld(original, -1, -1);
        Vec2 crossed = opposite + Rotate(new Vec2(-20, -12), 90);

        MapEditorRectBrushShape resized = MapEditorGeometry.ResizeRectBrush(original,
            MapEditorRectHandle.BOTTOM_RIGHT,
            new MapEditorPoint((int)crossed.X, (int)crossed.Y), MapEditorSnap.PIXELS_8);

        Assert.Equal(8, resized.Width);
        Assert.Equal(8, resized.Height);
        Vec2 anchored = RectHandleWorld(resized, -1, -1);
        Assert.InRange((anchored - opposite).Length(), 0, 1.5f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void RotatedEllipseResizeSnapsRadiiInLocalAxes(float rotation)
    {
        MapEditorEllipseBrushShape original = new(40, 50, 16, 8, rotation);
        Vec2 target = new Vec2(original.X, original.Y) + Rotate(new Vec2(39, 21), rotation);

        MapEditorEllipseBrushShape resized = MapEditorGeometry.ResizeEllipseBrush(original,
            new MapEditorPoint((int)MathF.Round(target.X), (int)MathF.Round(target.Y)),
            MapEditorSnap.PIXELS_8);

        Assert.Equal(new MapEditorEllipseBrushShape(40, 50, 40, 24, rotation), resized);
    }

    [Fact]
    public void RotatedEllipseHitUsesActualEllipseInsteadOfBounds()
    {
        MapEditorEllipseBrushShape ellipse = new(100, 100, 60, 10, 90);

        Assert.True(MapEditorGeometry.Contains(ellipse, new MapEditorPoint(100, 150)));
        Assert.False(MapEditorGeometry.Contains(ellipse, new MapEditorPoint(150, 100)));
    }

    [Fact]
    public void PolygonHitUsesConcaveGeometryAndReverseOrder()
    {
        MapEditorPolygonBrushShape concave = new([
            new MapEditorPoint(0, 0), new MapEditorPoint(40, 0),
            new MapEditorPoint(40, 10), new MapEditorPoint(10, 10),
            new MapEditorPoint(10, 40), new MapEditorPoint(0, 40),
        ]);
        MapEditorBrush bottom = Brush(1, MapEditorLayer.BACKGROUND,
            new MapEditorRectBrushShape(0, 0, 50, 50, 0));
        MapEditorBrush top = Brush(2, MapEditorLayer.BACKGROUND, concave);

        Assert.Equal(1, MapEditorGeometry.HitTestBrush([bottom, top], new MapEditorPoint(5, 30)));
        Assert.Equal(0, MapEditorGeometry.HitTestBrush([bottom, top], new MapEditorPoint(30, 30)));
    }

    [Fact]
    public void PolygonVertexOperationsSnapAndProtectValidity()
    {
        MapEditorPolygonBrushShape polygon = new([
            new MapEditorPoint(0, 0), new MapEditorPoint(32, 0),
            new MapEditorPoint(32, 32), new MapEditorPoint(0, 32),
        ]);

        MapEditorPolygonBrushShape moved = MapEditorGeometry.MovePolygonVertex(polygon, 2,
            new MapEditorPoint(23, 29), MapEditorSnap.PIXELS_8);
        MapEditorPolygonBrushShape inserted = MapEditorGeometry.InsertPolygonVertex(moved, 0,
            new MapEditorPoint(13, 2), MapEditorSnap.PIXELS_8);

        Assert.Equal(new MapEditorPoint(24, 32), moved.Vertices[2]);
        Assert.Equal(new MapEditorPoint(16, 0), inserted.Vertices[1]);
        Assert.True(MapEditorGeometry.TryRemovePolygonVertex(inserted, 1,
            out MapEditorPolygonBrushShape restored, out _));
        Assert.Equal(moved.Vertices, restored.Vertices);

        MapEditorPolygonBrushShape triangle = new([
            new MapEditorPoint(0, 0), new MapEditorPoint(10, 0), new MapEditorPoint(0, 10),
        ]);
        Assert.False(MapEditorGeometry.TryRemovePolygonVertex(triangle, 0, out _, out string? error));
        Assert.Contains("three distinct", error);
    }

    [Fact]
    public void ValidatorRejectsSelfIntersectionWithClearDiagnostic()
    {
        Assert.False(MapEditorBrushValidator.TryValidatePolygon([
            new MapEditorPoint(0, 0), new MapEditorPoint(20, 20),
            new MapEditorPoint(0, 20), new MapEditorPoint(20, 0),
        ], out string? error));

        Assert.Contains("self-intersect", error);
    }

    [Fact]
    public void RotatedZoneRectangleResizeSnapsLocalDimensionsAndKeepsOppositeAnchorFixed()
    {
        RectMapZoneShape original = new(10, 20, 20, 10, 90);
        Vec2 oppositeAnchor = new(25, 15);
        MapZoneDef zone = new() { Name = "zone", Shape = original };

        MapZoneDef changed = MapEditorGeometry.Scale(zone, oppositeAnchor,
            new Vec2(7, 47), MapEditorSnap.PIXELS_8);

        Assert.Equal(new RectMapZoneShape(1, 23, 32, 16, 90), changed.Shape);
        RectMapZoneShape resized = Assert.IsType<RectMapZoneShape>(changed.Shape);
        Vec2 center = MapEditorGeometry.Center(resized);
        Vec2 resizedOpposite = center + Rotate(
            new Vec2(-resized.Width / 2f, -resized.Height / 2f), resized.Rotation);
        Assert.InRange((resizedOpposite - oppositeAnchor).Length(), 0, 0.001f);
    }

    [Fact]
    public void RotatedEllipseAndCircleResizeSnapRadiiInTheirOwnGeometry()
    {
        MapZoneDef ellipse = new()
        {
            Name = "ellipse",
            Shape = new EllipseMapZoneShape(-3, 5, 7, 9, 90),
        };
        MapZoneDef circle = new()
        {
            Name = "circle",
            Shape = new CircleMapZoneShape(3, -5, 4),
        };

        MapZoneDef resizedEllipse = MapEditorGeometry.Scale(ellipse, new Vec2(-3, 5),
            new Vec2(-23, 17), MapEditorSnap.PIXELS_8);
        MapZoneDef resizedCircle = MapEditorGeometry.Scale(circle, new Vec2(3, -5),
            new Vec2(15, -5), MapEditorSnap.PIXELS_8);

        Assert.Equal(new EllipseMapZoneShape(-3, 5, 16, 24, 90), resizedEllipse.Shape);
        Assert.Equal(new CircleMapZoneShape(3, -5, 16), resizedCircle.Shape);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void BrushRectangleResizeSnapsInLocalAxesAndPreservesOppositeAnchorAndGrabOffset(
        float rotation)
    {
        MapEditorRectBrushShape original = new(-13, -21, 23, 17, rotation);
        Vec2 handle = RectHandleWorld(original, 1, 1);
        Vec2 opposite = RectHandleWorld(original, -1, -1);
        MapEditorPoint exactGrab = Point(handle);
        MapEditorPoint offsetGrab = new(exactGrab.X + 3, exactGrab.Y - 4);
        Vec2 worldDelta = Rotate(new Vec2(13, -9), rotation);
        MapEditorPoint exactPoint = Point(new Vec2(exactGrab.X, exactGrab.Y) + worldDelta);
        MapEditorPoint offsetPoint = new(exactPoint.X + 3, exactPoint.Y - 4);

        MapEditorRectBrushShape exact = MapEditorGeometry.ResizeRectBrush(original,
            MapEditorRectHandle.BOTTOM_RIGHT, exactGrab, exactPoint, MapEditorSnap.PIXELS_8);
        MapEditorRectBrushShape offset = MapEditorGeometry.ResizeRectBrush(original,
            MapEditorRectHandle.BOTTOM_RIGHT, offsetGrab, offsetPoint, MapEditorSnap.PIXELS_8);

        Assert.Equal(exact, offset);
        Assert.Equal(0, offset.Width % 8);
        Assert.Equal(0, offset.Height % 8);
        Assert.InRange((RectHandleWorld(offset, -1, -1) - opposite).Length(), 0, 1.5f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void BrushEllipseResizeSnapsLocalRadiiAndPreservesOppositeAnchorAndGrabOffset(
        float rotation)
    {
        MapEditorEllipseBrushShape original = new(-17, -9, 11, 7, rotation);
        Vec2 center = new(original.X, original.Y);
        Vec2 handle = center + Rotate(new Vec2(original.RadiusX, original.RadiusY), rotation);
        Vec2 opposite = center + Rotate(new Vec2(-original.RadiusX, -original.RadiusY), rotation);
        MapEditorPoint exactGrab = Point(handle);
        MapEditorPoint offsetGrab = new(exactGrab.X - 5, exactGrab.Y + 2);
        Vec2 worldDelta = Rotate(new Vec2(13, -9), rotation);
        MapEditorPoint exactPoint = Point(new Vec2(exactGrab.X, exactGrab.Y) + worldDelta);
        MapEditorPoint offsetPoint = new(exactPoint.X - 5, exactPoint.Y + 2);

        MapEditorEllipseBrushShape exact = MapEditorGeometry.ResizeEllipseBrush(original,
            exactGrab, exactPoint, MapEditorSnap.PIXELS_8);
        MapEditorEllipseBrushShape offset = MapEditorGeometry.ResizeEllipseBrush(original,
            offsetGrab, offsetPoint, MapEditorSnap.PIXELS_8);

        Assert.Equal(exact, offset);
        Assert.Equal(0, offset.RadiusX % 8);
        Assert.Equal(0, offset.RadiusY % 8);
        Vec2 resizedOpposite = new Vec2(offset.X, offset.Y) + Rotate(
            new Vec2(-offset.RadiusX, -offset.RadiusY), rotation);
        Assert.InRange((resizedOpposite - opposite).Length(), 0, 1.5f);
    }

    private static MapEditorBrush Brush(long id, MapEditorLayer layer,
        MapEditorBrushShape shape) => new(new MapEditorBrushId(id), $"brush-{id}", layer, shape,
        new MapEditorTextureMaterial(MapEditorTextureReference.Project("texture.png")),
        new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
            new MapEditorPoint(0, 0), 1, 1, 0), true);

    private static (int X, int Y) HandleDirections(MapEditorRectHandle handle) => handle switch
    {
        MapEditorRectHandle.TOP_LEFT => (-1, -1),
        MapEditorRectHandle.TOP_RIGHT => (1, -1),
        MapEditorRectHandle.BOTTOM_RIGHT => (1, 1),
        MapEditorRectHandle.BOTTOM_LEFT => (-1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(handle)),
    };

    private static Vec2 RectHandleWorld(MapEditorRectBrushShape rect, int directionX,
        int directionY)
    {
        Vec2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        return center + Rotate(new Vec2(directionX * rect.Width / 2f,
            directionY * rect.Height / 2f), rect.Rotation);
    }

    private static Vec2 Rotate(Vec2 point, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new Vec2(point.X * cosine - point.Y * sine,
            point.X * sine + point.Y * cosine);
    }

    private static MapEditorPoint Point(Vec2 point) => new(
        (int)MathF.Round(point.X, MidpointRounding.AwayFromZero),
        (int)MathF.Round(point.Y, MidpointRounding.AwayFromZero));
}
