using Godot;
using SimVec2 = Mortz.Core.Sim.Vec2;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorCanvasPicker
{
    private const float OVERLAP_CYCLE_TOLERANCE = 5f;

    private Vector2 _cycleLocalPoint;
    private Vector2 _cycleMapPoint;
    private int _cycleIndex = -1;
    private List<long> _cycleIds = [];

    public MapEditorBrush? PickBrush(MapEditorSnapshot? snapshot, MapEditorLayer layer,
        MapEditorBrushId? selectedId, MapEditorBrushDraft? preview, Vector2 point,
        float zoom, bool layerVisible, bool cycle, Vector2 pointerPressLocal,
        IReadOnlySet<MapEditorBrushId>? excluded = null)
    {
        if (snapshot?.BrushDocument == null || !layerVisible)
            return null;
        List<MapEditorBrush> hits = [];
        foreach (MapEditorBrush original in snapshot.BrushDocument.Layers.Get(layer).Brushes.Reverse())
        {
            if (excluded?.Contains(original.Id) == true)
                continue;
            MapEditorBrush brush = original.Id == selectedId && preview != null
                ? FromDraft(original.Id, preview)
                : original;
            if (!brush.Visible || !MapEditorGeometry.Contains(brush.Shape, Point(point)))
                continue;
            if (!cycle)
                return brush;
            hits.Add(brush);
        }

        if (hits.Count == 0)
            return null;
        return hits[PickIndex(hits.Select(brush => brush.Id.Value), point, zoom, cycle,
            pointerPressLocal)];
    }

    public MapEditorZone? PickZone(MapEditorSnapshot? snapshot, Vector2 point, float zoom,
        bool cycle, Vector2 pointerPressLocal)
    {
        if (snapshot == null)
            return null;
        List<MapEditorZone> hits = [];
        foreach (MapEditorZone zone in snapshot.Zones.Reverse())
        {
            if (zone.Shape.Compile().Contains(new SimVec2(point.X, point.Y)))
                hits.Add(zone);
        }

        if (hits.Count == 0)
            return null;
        return hits[PickIndex(hits.Select(zone => zone.Id.Value), point, zoom, cycle,
            pointerPressLocal)];
    }

    public MapEditorSpawn? PickSpawn(MapEditorSnapshot? snapshot, Vector2 point, float zoom,
        bool cycle, Vector2 pointerPressLocal)
    {
        if (snapshot == null)
            return null;
        float padding = 6f / zoom;
        List<MapEditorSpawn> hits = [];
        foreach (MapEditorSpawn spawn in snapshot.SpawnPoints.Reverse())
        {
            if (MapEditorCanvasProjection.SpawnBody(spawn.Value).Grow(padding).HasPoint(point))
                hits.Add(spawn);
        }

        if (hits.Count == 0)
            return null;
        return hits[PickIndex(hits.Select(spawn => spawn.Id.Value), point, zoom, cycle,
            pointerPressLocal)];
    }

    public void ResetIfPointerMoved(Vector2 localPoint)
    {
        if (localPoint.DistanceTo(_cycleLocalPoint) > OVERLAP_CYCLE_TOLERANCE)
            Reset();
    }

    public void Reset()
    {
        _cycleIds.Clear();
        _cycleIndex = -1;
    }

    private int PickIndex(IEnumerable<long> candidateIds, Vector2 mapPoint, float zoom,
        bool cycle, Vector2 pointerPressLocal)
    {
        if (!cycle)
            return 0;
        List<long> ids = candidateIds.ToList();
        bool continuation = _cycleIds.Count > 0 &&
                            _cycleMapPoint.DistanceTo(mapPoint) * zoom <= OVERLAP_CYCLE_TOLERANCE;
        _cycleIndex = continuation ? (_cycleIndex + 1) % ids.Count : 0;
        _cycleIds = ids;
        _cycleLocalPoint = pointerPressLocal;
        _cycleMapPoint = mapPoint;
        return _cycleIndex;
    }

    private static MapEditorPoint Point(Vector2 point) => new(
        (int)MathF.Round(point.X), (int)MathF.Round(point.Y));

    private static MapEditorBrush FromDraft(MapEditorBrushId id, MapEditorBrushDraft draft) =>
        new(id, draft.Name, draft.Layer, draft.Shape, draft.Material, draft.Projection, draft.Visible);
}
