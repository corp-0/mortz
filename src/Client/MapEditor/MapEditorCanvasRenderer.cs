using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorCanvasRenderFrame(
    MapEditorSnapshot? Snapshot,
    MapEditorCanvasCamera Camera,
    Texture2D PlayerTexture,
    MapEditorLayer SelectedLayer,
    MapEditorEditDomain EditDomain,
    MapEditorTool Tool,
    MapEditorZoneId? SelectedZoneId,
    MapEditorSpawnId? SelectedSpawnId,
    MapEditorBrushId? SelectedBrushId,
    MapEditorZoneDraft? ZonePreview,
    MapSpawnPoint? SpawnPreview,
    MapEditorBrushDraft? BrushPreview,
    IReadOnlyList<MapEditorBrushDraft> StampStroke,
    IReadOnlySet<MapEditorBrushId> HiddenBrushes,
    MapEditorZoneDraft? InspectorZonePreview,
    MapSpawnPoint? InspectorSpawnPreview,
    MapEditorBrushDraft? InspectorBrushPreview,
    bool PolygonCreating,
    bool ShowBackground,
    bool ShowSolid,
    bool ShowDestructible,
    bool ShowZones,
    bool ShowSpawns,
    bool ShowGrid,
    bool ShowBrushOutlines,
    bool CursorVisible,
    Vector2 CursorMapPosition);

public sealed class MapEditorCanvasRenderer(
    MapEditorCanvas canvas,
    MapEditorCanvasResources resources)
{
    private const float HANDLE_RADIUS = 7f;

    private static readonly Color _zoneFill = new(0.15f, 0.65f, 1f, 0.16f);
    private static readonly Color _zoneLine = new(0.25f, 0.8f, 1f, 0.9f);
    private static readonly Color _selectedFill = new(1f, 0.65f, 0.1f, 0.2f);
    private static readonly Color _selectedLine = new(1f, 0.75f, 0.2f);
    private static readonly Color _gridLine = new(0.72f, 0.78f, 0.9f, 0.18f);
    private static readonly Color _axisLine = new(0.9f, 0.92f, 1f, 0.55f);
    private static readonly Color _contextLine = new(0.72f, 0.75f, 0.8f, 0.42f);

    private readonly MapEditorCanvas _canvas = canvas;
    private readonly MapEditorCanvasResources _resources = resources;
    private MapEditorCanvasRenderFrame _frame = null!;

    private MapEditorSnapshot? Snapshot => _frame.Snapshot;
    private float Zoom => _frame.Camera.Zoom;
    private Vector2 Size => _canvas.Size;

    public void Draw(MapEditorCanvasRenderFrame frame)
    {
        _frame = frame;
        _canvas.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.035f, 0.04f, 0.05f));
        Rect2 mapRect = MapRect();
        DrawLayer(MapEditorLayer.BACKGROUND, frame.ShowBackground, mapRect);
        DrawLayer(MapEditorLayer.SOLID, frame.ShowSolid, mapRect);
        DrawLayer(MapEditorLayer.DESTRUCTIBLE, frame.ShowDestructible, mapRect);
        if (Snapshot == null)
            return;
        if (frame.ShowGrid)
            DrawGrid();
        DrawBakeBounds();
        if (frame.ShowBrushOutlines)
            DrawBrushes();

        if (frame.ShowZones)
            DrawZones();
        if (frame.ShowSpawns)
            DrawSpawns();
        if (frame.SpawnPreview == null && frame.Tool == MapEditorTool.SPAWN &&
            frame.CursorVisible)
            DrawSpawnPreview();
        DrawCursorHint();
    }

    private void DrawLayer(MapEditorLayer layer, bool visible, Rect2 mapRect)
    {
        if (!visible)
            return;
        if (Snapshot?.BrushDocument is { } document &&
            (_frame.SelectedLayer == layer || document.Layers.Get(layer).BakeDirty))
        {
            DrawSourceLayer(layer);
            return;
        }

        ImageTexture? baked = _resources.BakedTexture(layer);
        if (baked != null)
            _canvas.DrawTextureRect(baked, mapRect, false);
    }

    private void DrawZones()
    {
        foreach (MapEditorZone zone in Snapshot!.Zones)
        {
            MapEditorZoneDraft displayed = zone.Id == _frame.SelectedZoneId
                ? _frame.InspectorZonePreview ?? _frame.ZonePreview ?? Draft(zone)
                : Draft(zone);
            DrawZone(displayed, zone.Id == _frame.SelectedZoneId,
                _frame.EditDomain == MapEditorEditDomain.ZONES);
        }

        if (_frame.ZonePreview is { } created && _frame.SelectedZoneId == null)
            DrawZone(created, true, true);
    }

    private void DrawZone(MapEditorZoneDraft zone, bool selected, bool active)
    {
        Color fill = !active ? new Color(0.65f, 0.68f, 0.72f, 0.035f) :
            selected ? _selectedFill : _zoneFill;
        Color line = !active ? _contextLine : selected ? _selectedLine : _zoneLine;
        if (zone.Shape is RectMapZoneShape rect)
        {
            Vector2[] points = MapEditorCanvasProjection.ZoneRect(rect, MapToLocal);
            DrawShape(points, fill, line, selected, !active);
            if (selected && active)
            {
                foreach (Vector2 point in points)
                {
                    DrawHandle(point, false);
                }

                DrawHandle(MapToLocal(new Vector2(rect.X + rect.Width / 2f,
                    rect.Y + rect.Height / 2f)), true);
            }

            return;
        }

        Vector2 center;
        Vector2[] oval;
        Vector2 scaleHandle;
        if (zone.Shape is CircleMapZoneShape circle)
        {
            center = MapToLocal(new Vector2(circle.X, circle.Y));
            oval = MapEditorCanvasProjection.ZoneEllipse(circle.X, circle.Y, circle.Radius,
                circle.Radius, 0, MapToLocal);
            scaleHandle = center + new Vector2(circle.Radius * Zoom, 0);
        }
        else
        {
            EllipseMapZoneShape ellipse = (EllipseMapZoneShape)zone.Shape;
            center = MapToLocal(new Vector2(ellipse.X, ellipse.Y));
            oval = MapEditorCanvasProjection.ZoneEllipse(ellipse.X, ellipse.Y, ellipse.RadiusX,
                ellipse.RadiusY, ellipse.Rotation, MapToLocal);
            scaleHandle = center + (new Vector2(ellipse.RadiusX, ellipse.RadiusY) * Zoom)
                .Rotated(Mathf.DegToRad(ellipse.Rotation));
        }

        DrawShape(oval, fill, line, selected, !active);
        if (selected && active)
        {
            DrawHandle(center, true);
            DrawHandle(scaleHandle, false);
        }
    }

    private void DrawSpawns()
    {
        foreach (MapEditorSpawn entry in Snapshot!.SpawnPoints)
        {
            MapSpawnPoint spawn = entry.Id == _frame.SelectedSpawnId
                ? _frame.InspectorSpawnPreview ?? _frame.SpawnPreview ?? entry.Value
                : entry.Value;
            DrawSpawnEntry(spawn, entry.Id == _frame.SelectedSpawnId);
        }

        if (_frame.SpawnPreview is { } created && _frame.SelectedSpawnId == null)
            DrawSpawn(created, true);
    }

    private void DrawSpawnEntry(MapSpawnPoint spawn, bool selected)
    {
        Rect2 body = LocalSpawnBody(spawn);
        bool active = _frame.EditDomain == MapEditorEditDomain.SPAWNS;
        _canvas.DrawTextureRectRegion(_frame.PlayerTexture, body, new Rect2(0, 0, 32, 32),
            SpawnColor(spawn.Team, active ? selected ? 0.9f : 0.5f : 0.2f));
        if (selected && active)
            _canvas.DrawRect(body, Colors.White, false, 1f);
        else if (!active)
        {
            DrawDashedPolyline([
                    body.Position, body.Position + Vector2.Right * body.Size.X,
                    body.End, body.Position + Vector2.Down * body.Size.Y, body.Position
                ],
                _contextLine);
            _canvas.DrawLine(body.Position, body.End, _contextLine, 1f);
        }
    }

    private void DrawSpawn(MapSpawnPoint spawn, bool selected)
    {
        Rect2 body = LocalSpawnBody(spawn);
        _canvas.DrawTextureRectRegion(_frame.PlayerTexture, body, new Rect2(0, 0, 32, 32),
            SpawnColor(spawn.Team, selected ? 0.9f : 0.5f));
        if (selected)
            _canvas.DrawRect(body, Colors.White, false, 1f);
    }

    private void DrawSpawnPreview()
    {
        Rect2 body = LocalSpawnBody(new MapSpawnPoint(
            (int)_frame.CursorMapPosition.X, (int)_frame.CursorMapPosition.Y));
        _canvas.DrawTextureRectRegion(_frame.PlayerTexture, body, new Rect2(0, 0, 32, 32),
            new Color(1, 1, 1, 0.55f));
        _canvas.DrawRect(body, new Color(1, 1, 1, 0.65f), false, 1f);
    }

    private Rect2 LocalSpawnBody(MapSpawnPoint spawn)
    {
        Rect2 mapBody = MapEditorCanvasProjection.SpawnBody(spawn);
        return new Rect2(MapToLocal(mapBody.Position), mapBody.Size * Zoom);
    }

    private void DrawGrid()
    {
        Vector2 topLeft = LocalToMap(Vector2.Zero);
        Vector2 bottomRight = LocalToMap(Size);
        for (double x = Math.Floor(topLeft.X / MapEditorCanvas.GRID_SIZE) *
                        MapEditorCanvas.GRID_SIZE;
             x <= bottomRight.X;
             x += MapEditorCanvas.GRID_SIZE)
        {
            _canvas.DrawLine(MapToLocal(new Vector2((float)x, topLeft.Y)),
                MapToLocal(new Vector2((float)x, bottomRight.Y)), _gridLine, 1f);
        }

        for (double y = Math.Floor(topLeft.Y / MapEditorCanvas.GRID_SIZE) *
                        MapEditorCanvas.GRID_SIZE;
             y <= bottomRight.Y;
             y += MapEditorCanvas.GRID_SIZE)
        {
            _canvas.DrawLine(MapToLocal(new Vector2(topLeft.X, (float)y)),
                MapToLocal(new Vector2(bottomRight.X, (float)y)), _gridLine, 1f);
        }

        if (topLeft.X <= 0 && bottomRight.X >= 0)
            _canvas.DrawLine(MapToLocal(new Vector2(0, topLeft.Y)),
                MapToLocal(new Vector2(0, bottomRight.Y)), _axisLine, 2f);
        if (topLeft.Y <= 0 && bottomRight.Y >= 0)
            _canvas.DrawLine(MapToLocal(new Vector2(topLeft.X, 0)),
                MapToLocal(new Vector2(bottomRight.X, 0)), _axisLine, 2f);
    }

    private void DrawBakeBounds()
    {
        if (Size.X < 200 || Size.Y < 120)
            return;
        Rect2 rect = MapRect();
        DrawDashedPolyline([
                rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End,
                new Vector2(rect.Position.X, rect.End.Y), rect.Position
            ],
            new Color(0.95f, 0.95f, 1f, 0.72f), 2f, 8f, 5f);
        MapEditorMapBounds bounds = _frame.Camera.Bounds;
        string label = $"MAP {bounds.X}, {bounds.Y}  {bounds.Width} x {bounds.Height}";
        _canvas.DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(6, -7), label,
            HorizontalAlignment.Left, -1, 13, new Color(0.95f, 0.95f, 1f, 0.85f));
    }

    private void DrawBrushes()
    {
        if (Snapshot?.BrushDocument == null || !IsSelectedLayerVisible())
            return;
        MapEditorBounds visibleBounds = VisibleMapBounds();
        foreach (MapEditorBrush brush in Snapshot.BrushDocument.Layers.Get(
                     _frame.SelectedLayer).Brushes)
        {
            if (_frame.HiddenBrushes.Contains(brush.Id))
                continue;
            MapEditorBrushDraft displayed = brush.Id == _frame.SelectedBrushId &&
                                            DisplayedBrushPreview() is { } preview
                ? preview
                : Draft(brush);
            if (!displayed.Visible ||
                !MapEditorGeometry.Bounds(displayed.Shape).Intersects(visibleBounds))
                continue;
            DrawBrush(displayed.Shape, brush.Id == _frame.SelectedBrushId,
                _frame.EditDomain == MapEditorEditDomain.GEOMETRY);
        }

        if (_frame.SelectedBrushId == null && _frame.BrushPreview is { } draft &&
            (!_frame.PolygonCreating ||
             draft.Shape is MapEditorPolygonBrushShape { Vertices.Length: > 0 }) &&
            MapEditorGeometry.Bounds(draft.Shape).Intersects(visibleBounds))
        {
            DrawBrush(draft.Shape, true, true);
        }
        foreach (MapEditorBrushDraft stamp in _frame.StampStroke)
        {
            if (stamp.Visible && MapEditorGeometry.Bounds(stamp.Shape).Intersects(visibleBounds))
                DrawBrush(stamp.Shape, false, true);
        }
    }

    private void DrawBrush(MapEditorBrushShape shape, bool selected, bool active)
    {
        switch (shape)
        {
            case MapEditorRectBrushShape rect:
                DrawBrushPolygon(MapEditorCanvasProjection.BrushOutline(rect), shape,
                    selected, active);
                break;
            case MapEditorEllipseBrushShape ellipse:
                DrawBrushPolygon(MapEditorCanvasProjection.BrushOutline(ellipse), shape,
                    selected, active);
                break;
            case MapEditorPolygonBrushShape polygon:
                if (polygon.Vertices.Length >= 3)
                    DrawBrushPolygon(polygon.Vertices.Select(vertex =>
                        new Vector2(vertex.X, vertex.Y)).ToArray(), shape, selected, active);
                else if (!polygon.Vertices.IsEmpty)
                    _canvas.DrawPolyline(polygon.Vertices.Select(vertex => MapToLocal(
                        new Vector2(vertex.X, vertex.Y))).ToArray(), _selectedLine, 2f);
                if (selected && active)
                {
                    foreach (MapEditorPoint vertex in polygon.Vertices)
                    {
                        DrawHandle(MapToLocal(new Vector2(vertex.X, vertex.Y)), false);
                    }
                }

                break;
        }
    }

    private void DrawBrushPolygon(Vector2[] mapPoints, MapEditorBrushShape shape, bool selected,
        bool active)
    {
        Vector2[] points = mapPoints.Select(MapToLocal).ToArray();
        Vector2[] outline = [.. points, points[0]];
        if (active)
            _canvas.DrawPolyline(outline, selected ? _selectedLine : Colors.White,
                selected ? 2f : 1f);
        else
            DrawDashedPolyline(outline, _contextLine);
        if (!selected || !active)
            return;
        if (shape is MapEditorEllipseBrushShape ellipse)
        {
            DrawHandle(MapToLocal(new Vector2(ellipse.X, ellipse.Y)), true);
            Vector2 radius = new Vector2(ellipse.RadiusX, ellipse.RadiusY)
                .Rotated(Mathf.DegToRad(ellipse.Rotation));
            DrawHandle(MapToLocal(new Vector2(ellipse.X, ellipse.Y) + radius), false);
        }
        else if (shape is MapEditorRectBrushShape)
        {
            foreach (Vector2 point in points)
            {
                DrawHandle(point, false);
            }
        }
    }

    private void DrawSourceLayer(MapEditorLayer layer)
    {
        MapEditorLayerSource source = Snapshot!.BrushDocument!.Layers.Get(layer);
        MapEditorBounds visibleBounds = VisibleMapBounds();
        foreach (MapEditorBrush brush in source.Brushes)
        {
            MapEditorBrush displayed = layer == _frame.SelectedLayer &&
                                       brush.Id == _frame.SelectedBrushId && DisplayedBrushPreview() is { } replacement
                ? FromDraft(brush.Id, replacement)
                : brush;
            DrawPreviewBrush(displayed, visibleBounds);
        }

        if (layer == _frame.SelectedLayer && _frame.SelectedBrushId == null &&
            _frame.BrushPreview is { } created &&
            (!_frame.PolygonCreating ||
             created.Shape is MapEditorPolygonBrushShape { Vertices.Length: >= 3 }))
        {
            DrawPreviewBrush(FromDraft(new MapEditorBrushId(long.MaxValue), created),
                visibleBounds);
        }
    }

    private void DrawPreviewBrush(MapEditorBrush brush, MapEditorBounds visibleBounds)
    {
        if (!brush.Visible || !MapEditorGeometry.Bounds(brush.Shape).Intersects(visibleBounds))
            return;
        (MapEditorTextureData data, ImageTexture texture, bool missing) =
            _resources.Preview(brush.Material);
        DrawBrushMaterial(brush, data, texture, missing);
    }

    private void DrawBrushMaterial(MapEditorBrush brush, MapEditorTextureData textureData,
        Texture2D texture, bool missing)
    {
        Vector2[] mapPoints = MapEditorCanvasProjection.MaterialQuad(brush.Shape);
        if (mapPoints.Length < 3)
            return;
        Vector2[] points = mapPoints.Select(MapToLocal).ToArray();
        Vector2[] uvs = new Vector2[mapPoints.Length];
        for (int index = 0; index < mapPoints.Length; index++)
        {
            uvs[index] = missing
                ? MapToLocal(mapPoints[index]) / new Vector2(textureData.Width, textureData.Height)
                : MapEditorCanvasProjection.PreviewUv(brush, textureData, mapPoints[index]);
        }

        Color[] colors = brush.Shape is MapEditorEllipseBrushShape
            ?
            [
                new Color(0, 0, 0.125f, 0.625f),
                new Color(1, 0, 0.125f, 0.625f),
                new Color(1, 1, 0.125f, 0.625f),
                new Color(0, 1, 0.125f, 0.625f),
            ]
            : [Colors.White];
        _canvas.DrawPolygon(points, colors, uvs, texture);
    }

    private void DrawShape(Vector2[] points, Color fill, Color line, bool selected,
        bool dashed)
    {
        _canvas.DrawColoredPolygon(points, fill);
        Vector2[] outline = [.. points, points[0]];
        if (dashed)
            DrawDashedPolyline(outline, line);
        else
            _canvas.DrawPolyline(outline, line, selected ? 3f : 2f);
    }

    private void DrawDashedPolyline(Vector2[] points, Color color, float width = 1f,
        float dash = 6f, float gap = 4f)
    {
        for (int index = 0; index < points.Length - 1; index++)
        {
            Vector2 from = points[index];
            Vector2 to = points[index + 1];
            float length = from.DistanceTo(to);
            if (length <= 0)
                continue;
            Vector2 direction = (to - from) / length;
            for (float distance = 0; distance < length; distance += dash + gap)
            {
                _canvas.DrawLine(from + direction * distance,
                    from + direction * MathF.Min(length, distance + dash), color, width);
            }
        }
    }

    private void DrawCursorHint()
    {
        if (!_frame.CursorVisible || _frame.Tool == MapEditorTool.SELECT)
            return;
        string hint = _frame.Tool switch
        {
            MapEditorTool.RECT => "Zones - rectangle",
            MapEditorTool.CIRCLE => "Zones - ellipse",
            MapEditorTool.SPAWN => "Spawns - place",
            MapEditorTool.BRUSH_RECT => $"Geometry - {_frame.SelectedLayer} rectangle",
            MapEditorTool.BRUSH_ELLIPSE => $"Geometry - {_frame.SelectedLayer} ellipse",
            MapEditorTool.BRUSH_POLYGON => "Geometry - polygon - Enter to finish",
            MapEditorTool.STAMP => "Geometry - place stamp",
            _ => string.Empty,
        };
        if (hint.Length == 0)
            return;
        _canvas.DrawString(ThemeDB.FallbackFont,
            MapToLocal(_frame.CursorMapPosition) + new Vector2(14, 22), hint,
            HorizontalAlignment.Left, -1, 13, new Color(1f, 1f, 1f, 0.92f));
    }

    private void DrawHandle(Vector2 point, bool move)
    {
        Color color = move ? new Color(0.35f, 1f, 0.45f) : _selectedLine;
        _canvas.DrawCircle(point, HANDLE_RADIUS, color);
        _canvas.DrawCircle(point, HANDLE_RADIUS - 3f, new Color(0.08f, 0.09f, 0.1f));
    }

    private MapEditorBounds VisibleMapBounds()
    {
        Vector2 topLeft = LocalToMap(Vector2.Zero);
        Vector2 bottomRight = LocalToMap(Size);
        return new MapEditorBounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private bool IsSelectedLayerVisible() => _frame.SelectedLayer switch
    {
        MapEditorLayer.BACKGROUND => _frame.ShowBackground,
        MapEditorLayer.SOLID => _frame.ShowSolid,
        MapEditorLayer.DESTRUCTIBLE => _frame.ShowDestructible,
        _ => false,
    };

    private MapEditorBrushDraft? DisplayedBrushPreview() =>
        _frame.BrushPreview ?? _frame.InspectorBrushPreview;

    private Rect2 MapRect() => _frame.Camera.MapRect(Size);
    private Vector2 LocalToMap(Vector2 point) => _frame.Camera.LocalToMap(point, Size);
    private Vector2 MapToLocal(Vector2 point) => _frame.Camera.MapToLocal(point, Size);

    private static Color SpawnColor(Team? team, float alpha) => team switch
    {
        Team.BLUE => new Color(0.6f, 0.75f, 1f, alpha),
        Team.RED => new Color(1f, 0.6f, 0.6f, alpha),
        _ => new Color(1f, 1f, 1f, alpha),
    };

    private static MapEditorZoneDraft Draft(MapEditorZone zone) =>
        new(zone.Name, zone.Tags, zone.Shape, zone.Effects);

    private static MapEditorBrushDraft Draft(MapEditorBrush brush) => new(
        brush.Name, brush.Layer, brush.Shape, brush.Material, brush.Projection, brush.Visible);

    private static MapEditorBrush FromDraft(MapEditorBrushId id, MapEditorBrushDraft draft) =>
        new(id, draft.Name, draft.Layer, draft.Shape, draft.Material, draft.Projection,
            draft.Visible);
}
