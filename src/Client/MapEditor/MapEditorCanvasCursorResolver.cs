using Godot;
using SimVec2 = Mortz.Core.Sim.Vec2;

namespace Mortz.Client.MapEditor;

public class MapEditorCanvasCursorResolver(MapEditorCanvasPicker picker)
{
    public Control.CursorShape Resolve(MapEditorSnapshot snapshot, Vector2 point, float zoom,
        MapEditorTool tool, MapEditorEditDomain domain, MapEditorLayer layer,
        MapEditorBrushId? selectedBrushId, MapEditorBrushDraft? brushPreview,
        MapEditorBrush? selectedBrush, MapEditorZone? selectedZone, bool layerVisible,
        bool showSpawns, Vector2 pointerPressLocal, Control.CursorShape current)
    {
        if ((tool is MapEditorTool.SPAWN or MapEditorTool.SELECT) && showSpawns)
        {
            if (picker.PickSpawn(snapshot, point, zoom, false, pointerPressLocal) != null)
                return Control.CursorShape.Drag;
            if (tool == MapEditorTool.SPAWN)
                return Control.CursorShape.Cross;
        }
        if (tool == MapEditorTool.SELECT && selectedBrush != null &&
            BrushHandleCursor(selectedBrush.Shape, point, zoom) is { } brushCursor)
            return brushCursor;
        if (tool == MapEditorTool.SELECT && domain == MapEditorEditDomain.GEOMETRY &&
            picker.PickBrush(snapshot, layer, selectedBrushId, brushPreview, point, zoom,
                layerVisible, false, pointerPressLocal) != null)
            return Control.CursorShape.Drag;
        if (tool != MapEditorTool.SELECT || selectedZone == null)
            return tool == MapEditorTool.SELECT ? Control.CursorShape.Arrow : current;
        MapZoneHandle handle = MapEditorGeometry.PickHandle(selectedZone.Shape,
            new SimVec2(point.X, point.Y), 7f / zoom, out _);
        return handle switch
        {
            MapZoneHandle.MOVE => Control.CursorShape.Drag,
            MapZoneHandle.SCALE => Control.CursorShape.Cross,
            _ => Control.CursorShape.Arrow,
        };
    }

    private static Control.CursorShape? BrushHandleCursor(MapEditorBrushShape shape,
        Vector2 point, float zoom)
    {
        MapEditorPoint mapPoint = new((int)MathF.Round(point.X), (int)MathF.Round(point.Y));
        return shape switch
        {
            MapEditorRectBrushShape rect => MapEditorGeometry.PickRectBrushHandle(rect,
                mapPoint, 12f / zoom) switch
            {
                MapEditorRectHandle.MOVE => Control.CursorShape.Drag,
                MapEditorRectHandle.NONE => null,
                _ => Control.CursorShape.Cross,
            },
            MapEditorEllipseBrushShape ellipse => MapEditorGeometry.PickEllipseBrushHandle(
                ellipse, mapPoint, 12f / zoom) switch
            {
                MapEditorEllipseHandle.MOVE => Control.CursorShape.Drag,
                MapEditorEllipseHandle.NONE => null,
                _ => Control.CursorShape.Cross,
            },
            MapEditorPolygonBrushShape polygon when MapEditorGeometry.PickPolygonVertex(
                polygon, mapPoint, 12f / zoom) >= 0 => Control.CursorShape.Cross,
            _ => null,
        };
    }
}
