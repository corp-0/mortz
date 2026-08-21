using Godot;

namespace Mortz.Client.MapEditor;

public class MapEditorBrowserPresenter : IDisposable
{
    private readonly MapEditorObjectBrowser _browser;
    private readonly MapEditorCanvas _canvas;
    private readonly MapEditorInspectorPresenter _inspectors;
    private readonly MapEditorWorkspaceShell _shell;
    private MapEditorSnapshot? _snapshot;

    public MapEditorBrowserPresenter(MapEditorWorkspaceShell shell, MapEditorCanvas canvas,
        MapEditorInspectorPresenter inspectors)
    {
        _shell = shell;
        _browser = shell.ObjectBrowser;
        _canvas = canvas;
        _inspectors = inspectors;
        _browser.LayerSelected += SelectLayer;
        _browser.LayerVisibilityChanged += SetLayerVisibility;
        _browser.BrushVisibilityChanged += SetBrushVisibility;
        _browser.BrushSelected += SelectBrush;
        _browser.BrushReorderRequested += RequestBrushReorder;
        _browser.ZoneSelected += SelectZone;
        _browser.SpawnSelected += SelectSpawn;
        _browser.ZonesVisibilityChanged += SetZonesVisible;
        _browser.SpawnsVisibilityChanged += SetSpawnsVisible;
        _browser.BrushFrameRequested += _canvas.FrameBrush;
        _browser.ZoneFrameRequested += _canvas.FrameZone;
        _browser.SpawnFrameRequested += _canvas.FrameSpawn;
        _canvas.LayerSelectionChanged += ShowLayerSelection;
    }

    public event Action<MapEditorBrushId, MapEditorBrushDraft>? BrushReplaceRequested;
    public event Action<MapEditorBrushId, int>? BrushReorderRequested;
    public event Action? ViewChanged;

    public void Apply(MapEditorSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    public void ShowDomain(MapEditorEditDomain domain, bool rasterOnly)
    {
        _shell.ShowObjectBrowserState(domain, rasterOnly);
        Refresh();
    }

    public void SelectBrush(MapEditorLayer layer, MapEditorBrushId id)
    {
        _canvas.SelectLayer(layer);
        _canvas.SelectBrush(id);
        _inspectors.Refresh();
        Refresh();
    }

    public void SetVisibility(MapEditorViewLayer layer, bool visible)
    {
        switch (layer)
        {
            case MapEditorViewLayer.BACKGROUND:
                _canvas.ShowBackground = visible;
                break;
            case MapEditorViewLayer.SOLID:
                _canvas.ShowSolid = visible;
                break;
            case MapEditorViewLayer.DESTRUCTIBLE:
                _canvas.ShowDestructible = visible;
                break;
            case MapEditorViewLayer.ZONES:
                _canvas.ShowZones = visible;
                break;
            case MapEditorViewLayer.SPAWNS:
                _canvas.ShowSpawns = visible;
                break;
            case MapEditorViewLayer.GRID:
                _canvas.ShowGrid = visible;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
        _canvas.QueueRedraw();
        ViewChanged?.Invoke();
        Refresh();
    }

    public void Refresh()
    {
        if (_snapshot == null || !GodotObject.IsInstanceValid(_shell))
        {
            return;
        }
        _shell.PreserveObjectBrowserScroll(() => _browser.Apply(
            _snapshot, _canvas.EditDomain, _canvas.SelectedLayer,
            _canvas.SelectedBrushId, _canvas.SelectedZoneId, _canvas.SelectedSpawnId,
            _canvas.ShowBackground, _canvas.ShowSolid, _canvas.ShowDestructible,
            _canvas.ShowZones, _canvas.ShowSpawns));
    }

    public void Dispose()
    {
        _browser.LayerSelected -= SelectLayer;
        _browser.LayerVisibilityChanged -= SetLayerVisibility;
        _browser.BrushVisibilityChanged -= SetBrushVisibility;
        _browser.BrushSelected -= SelectBrush;
        _browser.BrushReorderRequested -= RequestBrushReorder;
        _browser.ZoneSelected -= SelectZone;
        _browser.SpawnSelected -= SelectSpawn;
        _browser.ZonesVisibilityChanged -= SetZonesVisible;
        _browser.SpawnsVisibilityChanged -= SetSpawnsVisible;
        _browser.BrushFrameRequested -= _canvas.FrameBrush;
        _browser.ZoneFrameRequested -= _canvas.FrameZone;
        _browser.SpawnFrameRequested -= _canvas.FrameSpawn;
        _canvas.LayerSelectionChanged -= ShowLayerSelection;
    }

    private void SelectLayer(MapEditorLayer layer)
    {
        _canvas.SelectLayer(layer);
        _inspectors.Refresh();
        Refresh();
    }

    private void ShowLayerSelection(MapEditorLayer _)
    {
        _inspectors.Refresh();
        Refresh();
    }

    private void SetLayerVisibility(MapEditorLayer layer, bool visible) => SetVisibility(
        layer switch
        {
            MapEditorLayer.BACKGROUND => MapEditorViewLayer.BACKGROUND,
            MapEditorLayer.SOLID => MapEditorViewLayer.SOLID,
            MapEditorLayer.DESTRUCTIBLE => MapEditorViewLayer.DESTRUCTIBLE,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        }, visible);

    private void SetBrushVisibility(MapEditorBrushId id, bool visible)
    {
        MapEditorBrush? brush = _inspectors.FindBrush(id);
        if (brush == null || brush.Visible == visible)
        {
            return;
        }
        BrushReplaceRequested?.Invoke(id, new MapEditorBrushDraft(brush.Name, brush.Layer,
            brush.Shape, brush.Material, brush.Projection, visible));
    }

    private void RequestBrushReorder(MapEditorBrushId id, int destination) =>
        BrushReorderRequested?.Invoke(id, destination);

    private void SelectZone(MapEditorZoneId id) => _canvas.Select(id);

    private void SelectSpawn(MapEditorSpawnId id) => _canvas.SelectSpawn(id);

    private void SetZonesVisible(bool visible) =>
        SetVisibility(MapEditorViewLayer.ZONES, visible);

    private void SetSpawnsVisible(bool visible) =>
        SetVisibility(MapEditorViewLayer.SPAWNS, visible);
}
