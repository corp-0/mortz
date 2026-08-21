using Mortz.Content;
using Mortz.Core.Sim;

namespace Mortz.Client.MapEditor;

public enum MapEditorZoneDrag
{
    MOVE,
    SCALE,
    CREATE_RECT,
    CREATE_CIRCLE,
}

public enum MapEditorBrushDrag
{
    CREATE_RECT,
    CREATE_ELLIPSE,
    MOVE,
    RESIZE,
    MOVE_VERTEX,
}

public enum MapEditorEditDomain
{
    GEOMETRY,
    ZONES,
    SPAWNS,
}

/// <summary>Presentation-local selection, drag state, and edit intent generation.</summary>
public sealed class MapEditorInteraction
{
    private MapEditorZoneDrag? _zoneDrag;
    private MapEditorZoneId? _draggedZoneId;
    private MapEditorZoneDraft? _zoneOriginal;
    private MapEditorSpawnId? _draggedSpawnId;
    private MapSpawnPoint? _spawnOriginal;
    private bool _creatingSpawn;
    private MapEditorBrushDrag? _brushDrag;
    private MapEditorBrushId? _draggedBrushId;
    private MapEditorBrushDraft? _brushOriginal;
    private MapEditorRectHandle _brushHandle;
    private int _polygonVertexIndex = -1;
    private bool _creatingPolygon;
    private bool _polygonEdgeInserted;
    private Vec2 _dragStart;
    private Vec2 _scaleAnchor;

    public event Action<MapEditorZoneId?>? ZoneSelectionChanged;
    public event Action<MapEditorSpawnId?>? SpawnSelectionChanged;
    public event Action<MapEditorZoneDraft?>? ZonePreviewChanged;
    public event Action<MapSpawnPoint?>? SpawnPreviewChanged;
    public event Action<MapEditorZoneDraft>? ZoneAddRequested;
    public event Action<MapEditorZoneId, MapEditorZoneDraft>? ZoneReplaceRequested;
    public event Action<MapSpawnPoint>? SpawnAddRequested;
    public event Action<MapEditorSpawnId, MapSpawnPoint>? SpawnReplaceRequested;
    public event Action<MapEditorLayer>? LayerSelectionChanged;
    public event Action<MapEditorBrushId?>? BrushSelectionChanged;
    public event Action<MapEditorBrushDraft?>? BrushPreviewChanged;
    public event Action<MapEditorBrushDraft>? BrushAddRequested;
    public event Action<MapEditorBrushId, MapEditorBrushDraft>? BrushReplaceRequested;
    public event Action<string?>? BrushDiagnosticChanged;
    public event Action<MapEditorEditDomain>? EditDomainChanged;

    public MapEditorSnapshot? Snapshot { get; private set; }
    public MapEditorZoneId? SelectedZoneId { get; private set; }
    public MapEditorSpawnId? SelectedSpawnId { get; private set; }
    public MapEditorZoneDraft? ZonePreview { get; private set; }
    public MapSpawnPoint? SpawnPreview { get; private set; }
    public MapEditorLayer SelectedLayer { get; private set; } = MapEditorLayer.BACKGROUND;
    public MapEditorBrushId? SelectedBrushId { get; private set; }
    public MapEditorBrushDraft? BrushPreview { get; private set; }
    public MapEditorSnap Snap { get; set; } = MapEditorSnap.PIXELS_8;
    public MapEditorEditDomain EditDomain { get; private set; } = MapEditorEditDomain.ZONES;
    public bool Dragging => _zoneDrag != null || _spawnOriginal != null || _brushDrag != null;
    public bool PolygonCreating => _creatingPolygon;
    public string? BrushDiagnostic { get; private set; }

    public void Apply(MapEditorUpdate update)
    {
        Snapshot = update.Snapshot;
        if (update.Change is MapEditorOpened or MapEditorReloaded)
        {
            Cancel();
            ClearAllSelections();
            SetEditDomain(update.Snapshot.CanEditBrushes
                ? MapEditorEditDomain.GEOMETRY
                : MapEditorEditDomain.ZONES);
            return;
        }

        if (update.Change is MapEditorBrushReplaced replaced &&
            _draggedBrushId == replaced.Id)
        {
            // A committed replacement is the new drag baseline. Do not retain the gesture
            // preview if the workspace applies the intent synchronously.
            Cancel();
        }

        if (SelectedZoneId is { } zoneId && !HasZone(zoneId))
        {
            if (_draggedZoneId == zoneId)
                Cancel();
            SelectZone(null);
        }
        if (SelectedSpawnId is { } spawnId && !HasSpawn(spawnId))
        {
            if (_draggedSpawnId == spawnId)
                Cancel();
            SelectSpawn(null);
        }
        if (SelectedBrushId is { } brushId && !HasBrush(brushId, SelectedLayer))
        {
            if (_draggedBrushId == brushId)
                Cancel();
            SelectBrush(null);
        }

        if (update.Change is MapEditorZoneAdded zoneAdded)
            SelectZone(zoneAdded.Id);
        else if (update.Change is MapEditorSpawnAdded spawnAdded)
            SelectSpawn(spawnAdded.Id);
        else if (update.Change is MapEditorBrushAdded brushAdded)
            SelectBrush(brushAdded.Id);
    }

    public void SetEditDomain(MapEditorEditDomain domain)
    {
        if (EditDomain == domain)
            return;
        Cancel();
        ClearAllSelections();
        EditDomain = domain;
        EditDomainChanged?.Invoke(domain);
    }

    public void SelectLayer(MapEditorLayer layer)
    {
        if (SelectedLayer == layer)
            return;
        Cancel();
        SelectedLayer = layer;
        if (SelectedBrushId != null)
            SelectBrush(null);
        LayerSelectionChanged?.Invoke(layer);
    }

    public void SelectBrush(MapEditorBrushId? id)
    {
        if (id != null && EditDomain != MapEditorEditDomain.GEOMETRY)
            id = null;
        if (id is { } value && !HasBrush(value, SelectedLayer))
            id = null;
        if (SelectedBrushId == id)
            return;
        if (id != null)
        {
            ClearZoneSelection();
            ClearSpawnSelection();
        }
        SelectedBrushId = id;
        BrushSelectionChanged?.Invoke(id);
    }

    public void SelectZone(MapEditorZoneId? id)
    {
        if (id != null && EditDomain != MapEditorEditDomain.ZONES)
            id = null;
        if (id is { } value && !HasZone(value))
            id = null;
        if (SelectedZoneId == id)
            return;
        if (id != null && SelectedBrushId != null)
        {
            SelectedBrushId = null;
            BrushSelectionChanged?.Invoke(null);
        }
        if (id != null && SelectedSpawnId != null)
        {
            SelectedSpawnId = null;
            SpawnSelectionChanged?.Invoke(null);
        }
        SelectedZoneId = id;
        ZoneSelectionChanged?.Invoke(id);
    }

    public void SelectSpawn(MapEditorSpawnId? id)
    {
        if (id != null && EditDomain != MapEditorEditDomain.SPAWNS)
            id = null;
        if (id is { } value && !HasSpawn(value))
            id = null;
        if (SelectedSpawnId == id)
            return;
        if (id != null && SelectedBrushId != null)
        {
            SelectedBrushId = null;
            BrushSelectionChanged?.Invoke(null);
        }
        if (id != null && SelectedZoneId != null)
        {
            SelectedZoneId = null;
            ZoneSelectionChanged?.Invoke(null);
        }
        SelectedSpawnId = id;
        SpawnSelectionChanged?.Invoke(id);
    }

    public bool BeginZoneDrag(MapEditorZoneId id, MapEditorZoneDrag drag,
        Vec2 start, Vec2 scaleAnchor = default)
    {
        MapEditorZone? zone = Snapshot?.Zones.FirstOrDefault(candidate => candidate.Id == id);
        if (zone == null)
            return false;
        Cancel();
        SelectZone(id);
        _draggedZoneId = id;
        _zoneOriginal = Draft(zone);
        _zoneDrag = drag;
        _dragStart = start;
        _scaleAnchor = scaleAnchor;
        SetZonePreview(_zoneOriginal);
        return true;
    }

    public void BeginZoneCreation(MapEditorZoneDrag drag, MapEditorZoneDraft draft, Vec2 start)
    {
        if (drag is not (MapEditorZoneDrag.CREATE_RECT or MapEditorZoneDrag.CREATE_CIRCLE))
            throw new ArgumentOutOfRangeException(nameof(drag));
        Cancel();
        SelectZone(null);
        _zoneOriginal = draft;
        _zoneDrag = drag;
        _dragStart = start;
        SetZonePreview(draft);
    }

    public bool BeginSpawnMove(MapEditorSpawnId id, Vec2 start)
    {
        MapEditorSpawn? spawn = FindSpawn(id);
        if (spawn == null)
            return false;
        Cancel();
        SelectSpawn(id);
        _draggedSpawnId = id;
        _spawnOriginal = spawn.Value.Value;
        _dragStart = start;
        SetSpawnPreview(_spawnOriginal);
        return true;
    }

    public void BeginSpawnCreation(MapSpawnPoint spawn, Vec2 start)
    {
        Cancel();
        SelectSpawn(null);
        _creatingSpawn = true;
        _spawnOriginal = spawn;
        _dragStart = start;
        SetSpawnPreview(spawn);
    }

    public void BeginBrushCreation(MapEditorBrushDraft draft, MapEditorPoint start)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Layer != SelectedLayer ||
            draft.Shape is not (MapEditorRectBrushShape or MapEditorEllipseBrushShape))
            throw new ArgumentException("Brush creation must target the selected layer.",
                nameof(draft));
        Cancel();
        SelectBrush(null);
        _brushOriginal = draft;
        _brushDrag = draft.Shape is MapEditorRectBrushShape
            ? MapEditorBrushDrag.CREATE_RECT : MapEditorBrushDrag.CREATE_ELLIPSE;
        _dragStart = new Vec2(start.X, start.Y);
        SetBrushPreview(draft);
    }

    public void BeginPolygonCreation(MapEditorBrushDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Layer != SelectedLayer || draft.Shape is not MapEditorPolygonBrushShape)
            throw new ArgumentException("Polygon creation must target the selected layer.",
                nameof(draft));
        Cancel();
        SelectBrush(null);
        _creatingPolygon = true;
        _brushOriginal = draft;
        SetBrushDiagnostic(null);
        SetBrushPreview(draft);
    }

    public void AppendPolygonVertex(MapEditorPoint point)
    {
        if (!_creatingPolygon || _brushOriginal?.Shape is not MapEditorPolygonBrushShape polygon)
            return;
        MapEditorPoint snapped = MapEditorGeometry.Snap(point, Snap);
        polygon = polygon with { Vertices = polygon.Vertices.Add(snapped) };
        _brushOriginal = _brushOriginal with { Shape = polygon };
        SetBrushDiagnostic(null);
        SetBrushPreview(_brushOriginal);
    }

    public bool RemoveLastPolygonVertex()
    {
        if (!_creatingPolygon || _brushOriginal?.Shape is not MapEditorPolygonBrushShape polygon ||
            polygon.Vertices.IsEmpty)
            return false;
        _brushOriginal = _brushOriginal with
        {
            Shape = polygon with { Vertices = polygon.Vertices.RemoveAt(polygon.Vertices.Length - 1) },
        };
        SetBrushDiagnostic(null);
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool TryCommitPolygonCreation()
    {
        if (!_creatingPolygon || _brushOriginal?.Shape is not MapEditorPolygonBrushShape polygon)
            return false;
        if (!MapEditorBrushValidator.TryValidatePolygon(polygon.Vertices, out string? error))
        {
            SetBrushDiagnostic(error);
            return false;
        }
        MapEditorBrushDraft committed = _brushOriginal;
        ClearDrag();
        BrushAddRequested?.Invoke(committed);
        return true;
    }

    public bool BeginBrushDrag(MapEditorBrushId id, MapEditorRectHandle handle,
        MapEditorPoint start)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush?.Shape is not MapEditorRectBrushShape || handle == MapEditorRectHandle.NONE)
            return false;
        return BeginBrushDrag(id, Draft(brush), handle, start);
    }

    public bool BeginBrushDrag(MapEditorBrushId id, MapEditorBrushDraft original,
        MapEditorRectHandle handle, MapEditorPoint start)
    {
        if (!HasBrush(id, SelectedLayer) || original.Layer != SelectedLayer ||
            original.Shape is not MapEditorRectBrushShape ||
            handle == MapEditorRectHandle.NONE)
            return false;
        Cancel();
        SelectBrush(id);
        _draggedBrushId = id;
        _brushOriginal = original;
        _brushDrag = handle == MapEditorRectHandle.MOVE
            ? MapEditorBrushDrag.MOVE : MapEditorBrushDrag.RESIZE;
        _brushHandle = handle;
        _dragStart = new Vec2(start.X, start.Y);
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool BeginBrushMove(MapEditorBrushId id, MapEditorPoint start)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush == null)
            return false;
        return BeginBrushMove(id, Draft(brush), start);
    }

    public bool BeginBrushMove(MapEditorBrushId id, MapEditorBrushDraft original,
        MapEditorPoint start)
    {
        if (!HasBrush(id, SelectedLayer) || original.Layer != SelectedLayer)
            return false;
        Cancel();
        SelectBrush(id);
        _draggedBrushId = id;
        _brushOriginal = original;
        _brushDrag = MapEditorBrushDrag.MOVE;
        _dragStart = new Vec2(start.X, start.Y);
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool BeginBrushDrag(MapEditorBrushId id, MapEditorEllipseHandle handle,
        MapEditorPoint start)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush?.Shape is not MapEditorEllipseBrushShape || handle == MapEditorEllipseHandle.NONE)
            return false;
        return BeginBrushDrag(id, Draft(brush), handle, start);
    }

    public bool BeginBrushDrag(MapEditorBrushId id, MapEditorBrushDraft original,
        MapEditorEllipseHandle handle, MapEditorPoint start)
    {
        if (!HasBrush(id, SelectedLayer) || original.Layer != SelectedLayer ||
            original.Shape is not MapEditorEllipseBrushShape ||
            handle == MapEditorEllipseHandle.NONE)
            return false;
        Cancel();
        SelectBrush(id);
        _draggedBrushId = id;
        _brushOriginal = original;
        _brushDrag = handle == MapEditorEllipseHandle.MOVE
            ? MapEditorBrushDrag.MOVE : MapEditorBrushDrag.RESIZE;
        _dragStart = new Vec2(start.X, start.Y);
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool BeginPolygonVertexDrag(MapEditorBrushId id, int vertexIndex,
        MapEditorPoint start)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush?.Shape is not MapEditorPolygonBrushShape polygon ||
            (uint)vertexIndex >= (uint)polygon.Vertices.Length)
            return false;
        Cancel();
        SelectBrush(id);
        _draggedBrushId = id;
        _brushOriginal = Draft(brush);
        _polygonVertexIndex = vertexIndex;
        _brushDrag = MapEditorBrushDrag.MOVE_VERTEX;
        _dragStart = new Vec2(start.X, start.Y);
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool BeginPolygonEdgeInsertion(MapEditorBrushId id, int edgeIndex,
        MapEditorPoint point)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush?.Shape is not MapEditorPolygonBrushShape polygon ||
            (uint)edgeIndex >= (uint)polygon.Vertices.Length)
            return false;
        Cancel();
        SelectBrush(id);
        _draggedBrushId = id;
        _brushOriginal = Draft(brush);
        _polygonVertexIndex = edgeIndex + 1;
        _brushDrag = MapEditorBrushDrag.MOVE_VERTEX;
        _dragStart = new Vec2(point.X, point.Y);
        MapEditorPolygonBrushShape inserted = MapEditorGeometry.InsertPolygonVertex(
            polygon, edgeIndex, point, Snap);
        _brushOriginal = _brushOriginal with { Shape = inserted };
        _polygonEdgeInserted = true;
        SetBrushPreview(_brushOriginal);
        return true;
    }

    public bool RemovePolygonVertex(MapEditorBrushId id, int vertexIndex)
    {
        MapEditorBrush? brush = FindBrush(id, SelectedLayer);
        if (brush?.Shape is not MapEditorPolygonBrushShape polygon)
            return false;
        if (!MapEditorGeometry.TryRemovePolygonVertex(polygon, vertexIndex,
                out MapEditorPolygonBrushShape replacement, out string? error))
        {
            SetBrushDiagnostic(error);
            return false;
        }
        SetBrushDiagnostic(null);
        BrushReplaceRequested?.Invoke(id, Draft(brush) with { Shape = replacement });
        return true;
    }

    public void Update(Vec2 point)
    {
        if (_brushDrag != null && _brushOriginal != null)
        {
            MapEditorPoint current = new((int)MathF.Round(point.X), (int)MathF.Round(point.Y));
            MapEditorPoint start = new((int)MathF.Round(_dragStart.X),
                (int)MathF.Round(_dragStart.Y));
            MapEditorBrushDraft changed = (_brushDrag, _brushOriginal.Shape) switch
            {
                (MapEditorBrushDrag.CREATE_RECT, MapEditorRectBrushShape) => _brushOriginal with
                {
                    Shape = MapEditorGeometry.RectBrushFromCorners(start, current, Snap),
                },
                (MapEditorBrushDrag.CREATE_ELLIPSE, MapEditorEllipseBrushShape) =>
                    _brushOriginal with
                    {
                        Shape = MapEditorGeometry.EllipseBrushFromCenter(start, current, Snap),
                    },
                (MapEditorBrushDrag.MOVE, _) => MoveDraft(_brushOriginal,
                    MapEditorGeometry.SnappedDelta(start, current, Snap)),
                (MapEditorBrushDrag.RESIZE, MapEditorRectBrushShape rect) => _brushOriginal with
                {
                    Shape = MapEditorGeometry.ResizeRectBrush(rect, _brushHandle, start,
                        current, Snap),
                },
                (MapEditorBrushDrag.RESIZE, MapEditorEllipseBrushShape ellipse) =>
                    _brushOriginal with
                    {
                        Shape = MapEditorGeometry.ResizeEllipseBrush(ellipse, start, current,
                            Snap),
                    },
                (MapEditorBrushDrag.MOVE_VERTEX, MapEditorPolygonBrushShape polygon) =>
                    (_brushOriginal with
                    {
                        Shape = MapEditorGeometry.MovePolygonVertex(polygon,
                            _polygonVertexIndex, current, Snap),
                    }),
                _ => _brushOriginal,
            };
            SetBrushPreview(changed);
            return;
        }
        if (_zoneDrag != null && _zoneOriginal != null)
        {
            MapEditorZoneDraft changed = _zoneDrag switch
            {
                MapEditorZoneDrag.CREATE_RECT => _zoneOriginal with
                {
                    Shape = MapEditorGeometry.RectFromCorners(
                        SnapPoint(_dragStart), SnapPoint(point)),
                },
                MapEditorZoneDrag.CREATE_CIRCLE => _zoneOriginal with
                {
                    Shape = MapEditorGeometry.EllipseFromCenter(
                        SnapPoint(_dragStart), SnapPoint(point)),
                },
                MapEditorZoneDrag.SCALE => FromDef(MapEditorGeometry.Scale(
                    ToDef(_zoneOriginal), _scaleAnchor, point, Snap)),
                MapEditorZoneDrag.MOVE => FromDef(MapEditorGeometry.Move(
                    ToDef(_zoneOriginal), SnappedGesturePoint(point) - _dragStart)),
                _ => _zoneOriginal,
            };
            SetZonePreview(changed);
            return;
        }

        if (_spawnOriginal is { } spawn && Snapshot != null)
        {
            if (_creatingSpawn)
            {
                Vec2 snapped = SnapPoint(point);
                SetSpawnPreview(spawn with { X = (int)snapped.X, Y = (int)snapped.Y });
            }
            else
                SetSpawnPreview(MapEditorGeometry.MoveSpawn(spawn, _dragStart,
                    SnappedGesturePoint(point)));
        }
    }

    public void Commit(Vec2 point)
    {
        Update(point);
        if (_brushDrag != null && _brushOriginal != null && BrushPreview != null)
        {
            MapEditorBrushDraft preview = BrushPreview;
            if (_draggedBrushId is { } id)
            {
                if (_polygonEdgeInserted || !BrushEquals(_brushOriginal, preview))
                {
                    if (preview.Shape is MapEditorPolygonBrushShape polygon &&
                        !MapEditorBrushValidator.TryValidatePolygon(polygon.Vertices,
                            out string? error))
                    {
                        SetBrushDiagnostic(error);
                        return;
                    }
                    BrushReplaceRequested?.Invoke(id, preview);
                }
            }
            else
                BrushAddRequested?.Invoke(preview);
        }
        else if (_zoneDrag != null && _zoneOriginal != null && ZonePreview != null)
        {
            MapEditorZoneDraft preview = ZonePreview;
            if (_draggedZoneId is { } id)
            {
                if (!ZoneEquals(_zoneOriginal, preview))
                    ZoneReplaceRequested?.Invoke(id, preview);
            }
            else
            {
                ZoneAddRequested?.Invoke(preview);
            }
        }
        else if (_spawnOriginal is { } original && SpawnPreview is { } spawn)
        {
            if (_creatingSpawn)
                SpawnAddRequested?.Invoke(spawn);
            else if (_draggedSpawnId is { } id && original != spawn)
                SpawnReplaceRequested?.Invoke(id, spawn);
        }
        ClearDrag();
    }

    public void Cancel() => ClearDrag();

    private bool HasZone(MapEditorZoneId id) =>
        Snapshot?.Zones.Any(zone => zone.Id == id) == true;

    private bool HasSpawn(MapEditorSpawnId id) =>
        Snapshot?.SpawnPoints.Any(spawn => spawn.Id == id) == true;

    private bool HasBrush(MapEditorBrushId id, MapEditorLayer layer) =>
        Snapshot?.BrushDocument?.Layers.Get(layer).Brushes.Any(brush => brush.Id == id) == true;

    private MapEditorBrush? FindBrush(MapEditorBrushId id, MapEditorLayer layer) =>
        Snapshot?.BrushDocument?.Layers.Get(layer).Brushes.FirstOrDefault(brush => brush.Id == id);

    private MapEditorSpawn? FindSpawn(MapEditorSpawnId id)
    {
        if (Snapshot == null)
            return null;
        foreach (MapEditorSpawn spawn in Snapshot.SpawnPoints)
        {
            if (spawn.Id == id)
                return spawn;
        }
        return null;
    }

    private void ClearDrag()
    {
        _zoneDrag = null;
        _draggedZoneId = null;
        _zoneOriginal = null;
        _draggedSpawnId = null;
        _spawnOriginal = null;
        _creatingSpawn = false;
        _brushDrag = null;
        _draggedBrushId = null;
        _brushOriginal = null;
        _brushHandle = MapEditorRectHandle.NONE;
        _polygonVertexIndex = -1;
        _creatingPolygon = false;
        _polygonEdgeInserted = false;
        SetZonePreview(null);
        SetSpawnPreview(null);
        SetBrushPreview(null);
        SetBrushDiagnostic(null);
    }

    private void SetZonePreview(MapEditorZoneDraft? preview)
    {
        if (ZonePreview == preview)
            return;
        ZonePreview = preview;
        ZonePreviewChanged?.Invoke(preview);
    }

    private void SetSpawnPreview(MapSpawnPoint? preview)
    {
        if (SpawnPreview == preview)
            return;
        SpawnPreview = preview;
        SpawnPreviewChanged?.Invoke(preview);
    }

    private void SetBrushPreview(MapEditorBrushDraft? preview)
    {
        if (BrushPreview == preview)
            return;
        BrushPreview = preview;
        BrushPreviewChanged?.Invoke(preview);
    }

    private void SetBrushDiagnostic(string? diagnostic)
    {
        if (BrushDiagnostic == diagnostic)
            return;
        BrushDiagnostic = diagnostic;
        BrushDiagnosticChanged?.Invoke(diagnostic);
    }

    private void ClearZoneSelection()
    {
        if (SelectedZoneId == null)
            return;
        SelectedZoneId = null;
        ZoneSelectionChanged?.Invoke(null);
    }

    private void ClearSpawnSelection()
    {
        if (SelectedSpawnId == null)
            return;
        SelectedSpawnId = null;
        SpawnSelectionChanged?.Invoke(null);
    }

    private void ClearAllSelections()
    {
        ClearZoneSelection();
        ClearSpawnSelection();
        if (SelectedBrushId == null)
            return;
        SelectedBrushId = null;
        BrushSelectionChanged?.Invoke(null);
    }

    private Vec2 SnapPoint(Vec2 point) => new(
        MapEditorGeometry.Snap((int)MathF.Round(point.X), Snap),
        MapEditorGeometry.Snap((int)MathF.Round(point.Y), Snap));

    private Vec2 SnappedGesturePoint(Vec2 point)
    {
        MapEditorPoint delta = MapEditorGeometry.SnappedDelta(
            new MapEditorPoint((int)MathF.Round(_dragStart.X), (int)MathF.Round(_dragStart.Y)),
            new MapEditorPoint((int)MathF.Round(point.X), (int)MathF.Round(point.Y)), Snap);
        return _dragStart + new Vec2(delta.X, delta.Y);
    }

    private static MapEditorBrushDraft Draft(MapEditorBrush brush) => new(
        brush.Name, brush.Layer, brush.Shape, brush.Material, brush.Projection, brush.Visible);

    private static MapEditorBrushDraft MoveDraft(MapEditorBrushDraft draft, MapEditorPoint delta)
    {
        MapEditorBrush temporary = new(new MapEditorBrushId(1), draft.Name, draft.Layer,
            draft.Shape, draft.Material, draft.Projection, draft.Visible);
        MapEditorBrush moved = MapEditorGeometry.Move(temporary, delta.X, delta.Y);
        return Draft(moved);
    }

    private static bool BrushEquals(MapEditorBrushDraft left, MapEditorBrushDraft right) =>
        left.Name == right.Name && left.Layer == right.Layer && ShapesEqual(left.Shape, right.Shape) &&
        left.Material == right.Material && left.Projection == right.Projection &&
        left.Visible == right.Visible;

    private static bool ShapesEqual(MapEditorBrushShape left, MapEditorBrushShape right) =>
        left is MapEditorPolygonBrushShape leftPolygon &&
        right is MapEditorPolygonBrushShape rightPolygon
            ? leftPolygon.Vertices.SequenceEqual(rightPolygon.Vertices)
            : left == right;

    private static MapEditorZoneDraft Draft(MapEditorZone zone) =>
        new(zone.Name, zone.Tags, zone.Shape, zone.Effects);

    private static MapZoneDef ToDef(MapEditorZoneDraft zone) => new()
    {
        Name = zone.Name,
        Tags = zone.Tags.ToArray(),
        Shape = zone.Shape,
        Effects = zone.Effects.ToArray(),
    };

    private static MapEditorZoneDraft FromDef(MapZoneDef zone) =>
        new(zone.Name, [.. zone.Tags], zone.Shape, [.. zone.Effects]);

    private static bool ZoneEquals(MapEditorZoneDraft left, MapEditorZoneDraft right) =>
        left.Name == right.Name && left.Shape == right.Shape &&
        left.Tags.SequenceEqual(right.Tags) && left.Effects.SequenceEqual(right.Effects);

}
