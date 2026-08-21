using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public class MapEditorInspectorPresenter
{
    private readonly MapEditorCanvas _canvas;
    private readonly MapEditorWorkspaceShell _shell;
    private readonly Action _refreshBrowser;
    private IMapEditorTextureResolver _textureResolver = new MapEditorTextureResolver();
    private MapEditorSnapshot? _snapshot;

    public MapEditorInspectorPresenter(MapEditorCanvas canvas, MapEditorWorkspaceShell shell,
        PackedScene effectRowScene, Action refreshBrowser)
    {
        _canvas = canvas;
        _shell = shell;
        _refreshBrowser = refreshBrowser;
        Brush = shell.BrushInspector;
        Zone = shell.ZoneInspector;
        Spawn = shell.SpawnInspector;
        Zone.Configure(effectRowScene);

        Brush.PreviewRequested += (_, draft) => _canvas.PreviewSelectedBrush(draft);
        Brush.CommitRequested += (id, draft) => BrushReplaceRequested?.Invoke(id, draft);
        Brush.CancelRequested += _ => _canvas.PreviewSelectedBrush(null);
        Brush.RemoveRequested += id => BrushRemoveRequested?.Invoke(id);
        Brush.DuplicateRequested += id =>
            BrushDuplicateRequested?.Invoke(id, DuplicateOffset());
        Brush.MoveToLayerRequested += (id, layer) =>
            BrushMoveToLayerRequested?.Invoke(id, layer);

        Zone.PreviewRequested += (_, draft) => _canvas.PreviewSelectedZone(draft);
        Zone.CommitRequested += (id, draft) => ZoneReplaceRequested?.Invoke(id, draft);
        Zone.CancelRequested += _ => _canvas.PreviewSelectedZone(null);
        Zone.RemoveRequested += id => ZoneRemoveRequested?.Invoke(id);

        Spawn.PreviewRequested += (_, spawn) => _canvas.PreviewSelectedSpawn(spawn);
        Spawn.CommitRequested += (id, spawn) => SpawnReplaceRequested?.Invoke(id, spawn);
        Spawn.CancelRequested += _ => _canvas.PreviewSelectedSpawn(null);
        Spawn.RemoveRequested += id => SpawnRemoveRequested?.Invoke(id);
    }

    public MapEditorBrushInspector Brush { get; }
    public MapEditorZoneInspector Zone { get; }
    public MapEditorSpawnInspector Spawn { get; }

    public event Action<MapEditorBrushId, MapEditorBrushDraft>? BrushReplaceRequested;
    public event Action<MapEditorBrushId>? BrushRemoveRequested;
    public event Action<MapEditorBrushId, int>? BrushDuplicateRequested;
    public event Action<MapEditorBrushId, MapEditorLayer>? BrushMoveToLayerRequested;
    public event Action<MapEditorZoneId, MapEditorZoneDraft>? ZoneReplaceRequested;
    public event Action<MapEditorZoneId>? ZoneRemoveRequested;
    public event Action<MapEditorSpawnId, MapSpawnPoint>? SpawnReplaceRequested;
    public event Action<MapEditorSpawnId>? SpawnRemoveRequested;

    public void ConfigureTextureResolver(IMapEditorTextureResolver textureResolver)
    {
        _textureResolver = textureResolver ??
            throw new ArgumentNullException(nameof(textureResolver));
    }

    public void Apply(MapEditorSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    public void ShowZoneSelection(MapEditorZoneId? id)
    {
        if (Zone.SelectedId is { } previous && previous != id)
        {
            Zone.CancelDraft();
        }

        MapEditorZone? selected = id is { } value
            ? _snapshot?.Zones.FirstOrDefault(zone => zone.Id == value)
            : null;
        if (selected == null)
        {
            if (_canvas.SelectedSpawnId == null)
            {
                Mount(InspectorContent.HIDDEN);
            }

            _refreshBrowser();
            return;
        }

        Mount(InspectorContent.ZONE);
        Zone.Apply(new MapEditorZoneInspectorValue(selected.Id,
            new MapEditorZoneDraft(selected.Name, selected.Tags, selected.Shape, selected.Effects)));
        _refreshBrowser();
    }

    public void ShowSpawnSelection(MapEditorSpawnId? id)
    {
        if (Spawn.SelectedId is { } previous && previous != id)
        {
            Spawn.CancelDraft();
        }

        int index = id is { } value ? SpawnIndex(value) : -1;
        if (_snapshot == null || index < 0)
        {
            if (_canvas.SelectedZoneId == null)
            {
                Hide();
            }

            _refreshBrowser();
            return;
        }

        Mount(InspectorContent.SPAWN);
        Spawn.Apply(new MapEditorSpawnInspectorValue(id!.Value,
            _snapshot.SpawnPoints[index].Value, index + 1));
        _refreshBrowser();
    }

    public void ShowZonePreview(MapEditorZoneDraft? preview)
    {
        if (preview != null && _canvas.SelectedZoneId is { } id)
        {
            Zone.Apply(new MapEditorZoneInspectorValue(id, preview));
        }
        else if (preview == null)
        {
            Refresh();
        }
    }

    public void ShowSpawnPreview(MapSpawnPoint? preview)
    {
        if (preview is { } spawn && _canvas.SelectedSpawnId is { } id)
        {
            Spawn.Apply(new MapEditorSpawnInspectorValue(id, spawn, SpawnIndex(id) + 1));
        }
        else if (preview == null)
        {
            Refresh();
        }
    }

    public void ShowBrushSelection(MapEditorBrushId? id)
    {
        if (Brush.SelectedId is { } previous && previous != id)
        {
            Brush.CancelDraft();
        }

        MapEditorBrush? brush = FindSelectedBrush(id);
        if (brush == null)
        {
            ShowBrushDock();
            _refreshBrowser();
            return;
        }

        Mount(InspectorContent.BRUSH);
        ApplyBrush(brush, null);
        _refreshBrowser();
    }

    public void ShowBrushPreview(MapEditorBrushDraft? preview)
    {
        if (preview == null || _canvas.SelectedBrushId is not { } id)
        {
            ShowBrushSelection(_canvas.SelectedBrushId);
            return;
        }

        Brush.Apply(new MapEditorBrushInspectorValue(id, preview,
            MaterialMissing(preview.Material)));
    }

    public void ShowBrushDiagnostic(string? diagnostic)
    {
        MapEditorBrush? brush = FindSelectedBrush(_canvas.SelectedBrushId);
        if (brush != null)
        {
            ApplyBrush(brush, diagnostic);
        }
    }

    public void Refresh()
    {
        if (_canvas.SelectedZoneId is { } zoneId)
        {
            ShowZoneSelection(zoneId);
        }
        else if (_canvas.SelectedSpawnId is { } spawnId)
        {
            ShowSpawnSelection(spawnId);
        }
        else
        {
            Hide();
        }
    }

    public void DiscardDraft()
    {
        Brush.CancelDraft();
        Zone.CancelDraft();
        Spawn.CancelDraft();
        _canvas.PreviewSelectedBrush(null);
        _canvas.PreviewSelectedZone(null);
        _canvas.PreviewSelectedSpawn(null);
    }

    public MapEditorBrush? FindBrush(MapEditorBrushId id)
    {
        if (_snapshot?.BrushDocument == null)
        {
            return null;
        }

        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorBrush? brush = _snapshot.BrushDocument.Layers.Get(layer).Brushes
                .FirstOrDefault(candidate => candidate.Id == id);
            if (brush != null)
            {
                return brush;
            }
        }

        return null;
    }

    private void Hide()
    {
        if (_snapshot != null && _canvas.SelectedZoneId == null &&
            _canvas.SelectedSpawnId == null)
        {
            ShowBrushDock();
        }
        else
        {
            Mount(InspectorContent.HIDDEN);
        }
    }

    private void ShowBrushDock()
    {
        if (_snapshot == null || _canvas.SelectedZoneId != null ||
            _canvas.SelectedSpawnId != null)
        {
            return;
        }

        Mount(_canvas.EditDomain == MapEditorEditDomain.GEOMETRY &&
              _canvas.SelectedBrushId != null
            ? InspectorContent.BRUSH
            : InspectorContent.HIDDEN);
    }

    private void Mount(InspectorContent content)
    {
        switch (content)
        {
            case InspectorContent.BRUSH:
                _shell.ShowInspector(MapEditorInspectorKind.BRUSH);
                break;
            case InspectorContent.ZONE:
                _shell.ShowInspector(MapEditorInspectorKind.ZONE);
                break;
            case InspectorContent.SPAWN:
                _shell.ShowInspector(MapEditorInspectorKind.SPAWN);
                break;
            default:
                _shell.ShowInspector(MapEditorInspectorKind.EMPTY);
                break;
        }
    }

    private void ApplyBrush(MapEditorBrush brush, string? diagnostic)
    {
        Brush.Apply(new MapEditorBrushInspectorValue(brush.Id,
            new MapEditorBrushDraft(brush.Name, brush.Layer, brush.Shape, brush.Material,
                brush.Projection, brush.Visible), MaterialMissing(brush.Material), diagnostic));
    }

    private MapEditorBrush? FindSelectedBrush(MapEditorBrushId? id) =>
        id is { } value && _snapshot?.BrushDocument != null
            ? _snapshot.BrushDocument.Layers.Get(_canvas.SelectedLayer).Brushes
                .FirstOrDefault(brush => brush.Id == value)
            : null;

    private int SpawnIndex(MapEditorSpawnId id)
    {
        if (_snapshot == null)
        {
            return -1;
        }

        for (int i = 0; i < _snapshot.SpawnPoints.Length; i++)
        {
            if (_snapshot.SpawnPoints[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int DuplicateOffset() => _canvas.Snap == MapEditorSnap.NONE ? 1 : (int)_canvas.Snap;

    private bool MaterialMissing(MapEditorBrushMaterial material) =>
        material is MapEditorTextureMaterial texture &&
        !_textureResolver.Resolve(texture.Reference).IsResolved;

    private enum InspectorContent
    {
        HIDDEN,
        BRUSH,
        ZONE,
        SPAWN,
    }
}
