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

/// <summary>Presentation-local selection, drag state, and edit intent generation.</summary>
public sealed class MapEditorInteraction
{
    private MapEditorZoneDrag? _zoneDrag;
    private MapEditorZoneId? _draggedZoneId;
    private MapEditorZoneDraft? _zoneOriginal;
    private MapEditorSpawnId? _draggedSpawnId;
    private MapSpawnPoint? _spawnOriginal;
    private bool _creatingSpawn;
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

    public MapEditorSnapshot? Snapshot { get; private set; }
    public MapEditorZoneId? SelectedZoneId { get; private set; }
    public MapEditorSpawnId? SelectedSpawnId { get; private set; }
    public MapEditorZoneDraft? ZonePreview { get; private set; }
    public MapSpawnPoint? SpawnPreview { get; private set; }
    public bool Dragging => _zoneDrag != null || _spawnOriginal != null;

    public void Apply(MapEditorUpdate update)
    {
        Snapshot = update.Snapshot;
        if (update.Change is MapEditorOpened or MapEditorReloaded)
        {
            Cancel();
            SelectZone(null);
            SelectSpawn(null);
            return;
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

        if (update.Change is MapEditorZoneAdded zoneAdded)
            SelectZone(zoneAdded.Id);
        else if (update.Change is MapEditorSpawnAdded spawnAdded)
            SelectSpawn(spawnAdded.Id);
    }

    public void SelectZone(MapEditorZoneId? id)
    {
        if (id is { } value && !HasZone(value))
            id = null;
        if (SelectedZoneId == id)
            return;
        SelectedZoneId = id;
        ZoneSelectionChanged?.Invoke(id);
        if (id != null && SelectedSpawnId != null)
        {
            SelectedSpawnId = null;
            SpawnSelectionChanged?.Invoke(null);
        }
    }

    public void SelectSpawn(MapEditorSpawnId? id)
    {
        if (id is { } value && !HasSpawn(value))
            id = null;
        if (SelectedSpawnId == id)
            return;
        SelectedSpawnId = id;
        SpawnSelectionChanged?.Invoke(id);
        if (id != null && SelectedZoneId != null)
        {
            SelectedZoneId = null;
            ZoneSelectionChanged?.Invoke(null);
        }
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

    public void Update(Vec2 point)
    {
        if (_zoneDrag != null && _zoneOriginal != null)
        {
            MapEditorZoneDraft changed = _zoneDrag switch
            {
                MapEditorZoneDrag.CREATE_RECT => _zoneOriginal with
                {
                    Shape = MapEditorGeometry.RectFromCorners(_dragStart, point),
                },
                MapEditorZoneDrag.CREATE_CIRCLE => _zoneOriginal with
                {
                    Shape = MapEditorGeometry.EllipseFromCenter(_dragStart, point),
                },
                MapEditorZoneDrag.SCALE => FromDef(MapEditorGeometry.Scale(
                    ToDef(_zoneOriginal), _scaleAnchor, point)),
                MapEditorZoneDrag.MOVE => FromDef(MapEditorGeometry.Move(
                    ToDef(_zoneOriginal), point - _dragStart)),
                _ => _zoneOriginal,
            };
            SetZonePreview(changed);
            return;
        }

        if (_spawnOriginal is { } spawn && Snapshot != null)
        {
            SetSpawnPreview(MapEditorGeometry.MoveSpawn(spawn, _dragStart, point,
                Snapshot.Width, Snapshot.Height));
        }
    }

    public void Commit(Vec2 point)
    {
        Update(point);
        if (_zoneDrag != null && _zoneOriginal != null && ZonePreview != null)
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
        SetZonePreview(null);
        SetSpawnPreview(null);
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
