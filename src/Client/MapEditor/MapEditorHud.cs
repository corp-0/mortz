using System.Collections.Immutable;
using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public record MapEditorStatus(string Message, bool IsError = false);

public partial class MapEditorHud : Control, IMapEditorShortcutTarget
{
    [Export] private MapEditorTopBar _topBar = null!;
    [Export] private AcceptDialog _errorDialog = null!;
    [Export] private ConfirmationDialog _discardDialog = null!;
    [Export] private PackedScene _effectRowScene = null!;
    [Export] private MapEditorWorkspaceShell _workspaceShell = null!;
    private MapEditorCanvas _canvas = null!;
    private MapEditorToolbar _toolbar = null!;
    private MapEditorViewControls _viewControls = null!;
    private MapEditorInspectorPresenter _inspectors = null!;
    private MapEditorBrowserPresenter _browser = null!;
    private MapEditorShortcutHandler _shortcuts = null!;
    private MapEditorStampLibrary _stamps = null!;

    private MapEditorSnapshot? _snapshot;
    private bool _confirmingBrushInitialization;

    public event Action? SaveRequested;
    public event Action? ReloadRequested;
    public event Action? BackRequested;
    public event Action? DiscardConfirmed;
    public event Action? DiscardCancelled;
    public event Action<MapEditorZoneDraft>? ZoneAddRequested;
    public event Action<MapEditorZoneId, MapEditorZoneDraft>? ZoneReplaceRequested;
    public event Action<MapEditorZoneId>? ZoneRemoveRequested;
    public event Action<MapSpawnPoint>? SpawnAddRequested;
    public event Action<MapEditorSpawnId, MapSpawnPoint>? SpawnReplaceRequested;
    public event Action<MapEditorSpawnId>? SpawnRemoveRequested;
    public event Action? BrushSourceInitializationRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action<MapEditorBrushDraft>? BrushAddRequested;
    public event Action<ImmutableArray<MapEditorBrushDraft>>? BrushBatchAddRequested;
    public event Action<ImmutableArray<MapEditorBrushId>>? BrushBatchRemoveRequested;
    public event Action<MapEditorBrushId, MapEditorBrushDraft>? BrushReplaceRequested;
    public event Action<MapEditorBrushId>? BrushRemoveRequested;
    public event Action<MapEditorZoneId, int>? ZoneDuplicateRequested;
    public event Action<MapEditorSpawnId, int>? SpawnDuplicateRequested;
    public event Action<MapEditorBrushId, int>? BrushDuplicateRequested;
    public event Action<MapEditorBrushId, int>? BrushReorderRequested;
    public event Action<MapEditorBrushId, MapEditorLayer>? BrushMoveToLayerRequested;
    public event Action<MapEditorBrushId>? StampSaveRequested;
    public event Action<MapEditorStampId>? StampRemoveRequested;

    private int _cursorX;
    private int _cursorY;
    private float _zoom = 1f;

    public override void _Ready()
    {
        _canvas = _workspaceShell.Canvas;
        _toolbar = _workspaceShell.Toolbar;
        _viewControls = _workspaceShell.ViewControls;
        _stamps = _workspaceShell.StampLibrary;
        ConfigureWorkspaceShell();
        BuildInspectors();
        BuildBrowser();
        _shortcuts = new MapEditorShortcutHandler(this);
        _canvas.SelectionChanged += _inspectors.ShowZoneSelection;
        _canvas.SpawnSelectionChanged += _inspectors.ShowSpawnSelection;
        _canvas.ZonePreviewChanged += _inspectors.ShowZonePreview;
        _canvas.SpawnPreviewChanged += _inspectors.ShowSpawnPreview;
        _canvas.ZoneAddRequested += ForwardZoneAdd;
        _canvas.ZoneReplaceRequested += ForwardZoneReplace;
        _canvas.SpawnAddRequested += ForwardSpawnAdd;
        _canvas.SpawnReplaceRequested += ForwardSpawnReplace;
        _canvas.BrushSelectionChanged += ShowBrushSelection;
        _canvas.BrushPreviewChanged += _inspectors.ShowBrushPreview;
        _canvas.BrushAddRequested += draft => BrushAddRequested?.Invoke(draft);
        _canvas.BrushBatchAddRequested += drafts => BrushBatchAddRequested?.Invoke(drafts);
        _canvas.BrushBatchRemoveRequested += ids => BrushBatchRemoveRequested?.Invoke(ids);
        _canvas.BrushReplaceRequested += (id, draft) => BrushReplaceRequested?.Invoke(id, draft);
        _canvas.BrushDiagnosticChanged += _inspectors.ShowBrushDiagnostic;
        _canvas.CursorMoved += ShowCursorPosition;
        _canvas.ZoomChanged += ShowZoom;
        _canvas.EditDomainChanged += ShowEditDomain;
        _canvas.ToolChanged += ShowTool;
        _workspaceShell.Resized += UpdateResponsiveControls;
        _stamps.SaveSelectedRequested += OnStampSaveSelected;
        _stamps.StampSelected += SelectStamp;
        _stamps.StampRemoveRequested += OnStampRemove;
        _topBar.BackRequested += OnBackPressed;
        _topBar.ReloadRequested += OnReloadPressed;
        _topBar.SaveRequested += OnSavePressed;
        _topBar.ZoomOutRequested += OnZoomOutPressed;
        _topBar.ZoomResetRequested += OnZoomResetPressed;
        _topBar.ZoomInRequested += OnZoomInPressed;
        _topBar.FrameMapRequested += OnFrameMapPressed;
        _toolbar.DomainSelected += SelectDomain;
        _toolbar.ToolSelected += SelectTool;
        _viewControls.SnapSelected += SetSnap;
        _viewControls.ViewVisibilityChanged += SetViewVisibility;
        _viewControls.ResetZoomRequested += OnZoomResetPressed;
        _viewControls.FrameMapRequested += OnFrameMapPressed;
        _workspaceShell.BrushInitializationRequested += OnInitializeBrushLayersPressed;
        _workspaceShell.ProblemActivated += ActivateProblem;
        _discardDialog.Confirmed += OnDiscardConfirmed;
        _discardDialog.Canceled += OnDiscardCancelled;
        SetProcessUnhandledInput(Visible);
    }

    public override void _ExitTree()
    {
        _canvas.SelectionChanged -= _inspectors.ShowZoneSelection;
        _canvas.SpawnSelectionChanged -= _inspectors.ShowSpawnSelection;
        _canvas.ZonePreviewChanged -= _inspectors.ShowZonePreview;
        _canvas.SpawnPreviewChanged -= _inspectors.ShowSpawnPreview;
        _canvas.ZoneAddRequested -= ForwardZoneAdd;
        _canvas.ZoneReplaceRequested -= ForwardZoneReplace;
        _canvas.SpawnAddRequested -= ForwardSpawnAdd;
        _canvas.SpawnReplaceRequested -= ForwardSpawnReplace;
        _canvas.BrushSelectionChanged -= _inspectors.ShowBrushSelection;
        _canvas.BrushPreviewChanged -= _inspectors.ShowBrushPreview;
        _canvas.BrushDiagnosticChanged -= _inspectors.ShowBrushDiagnostic;
        _canvas.CursorMoved -= ShowCursorPosition;
        _canvas.ZoomChanged -= ShowZoom;
        _canvas.EditDomainChanged -= ShowEditDomain;
        _canvas.ToolChanged -= ShowTool;
        _workspaceShell.Resized -= UpdateResponsiveControls;
        _topBar.BackRequested -= OnBackPressed;
        _topBar.ReloadRequested -= OnReloadPressed;
        _topBar.SaveRequested -= OnSavePressed;
        _topBar.ZoomOutRequested -= OnZoomOutPressed;
        _topBar.ZoomResetRequested -= OnZoomResetPressed;
        _topBar.ZoomInRequested -= OnZoomInPressed;
        _topBar.FrameMapRequested -= OnFrameMapPressed;
        _toolbar.DomainSelected -= SelectDomain;
        _toolbar.ToolSelected -= SelectTool;
        _viewControls.SnapSelected -= SetSnap;
        _viewControls.ViewVisibilityChanged -= SetViewVisibility;
        _viewControls.ResetZoomRequested -= OnZoomResetPressed;
        _viewControls.FrameMapRequested -= OnFrameMapPressed;
        _stamps.SaveSelectedRequested -= OnStampSaveSelected;
        _stamps.StampSelected -= SelectStamp;
        _stamps.StampRemoveRequested -= OnStampRemove;
        _workspaceShell.BrushInitializationRequested -= OnInitializeBrushLayersPressed;
        _workspaceShell.ProblemActivated -= ActivateProblem;
        _browser.Dispose();
        _discardDialog.Confirmed -= OnDiscardConfirmed;
        _discardDialog.Canceled -= OnDiscardCancelled;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_shortcuts.Handle(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShowForEditor()
    {
        Show();
        SetProcessUnhandledInput(true);
    }

    public void HideForFlow()
    {
        Hide();
        SetProcessUnhandledInput(false);
    }

    public void OnReloadPressed() => ReloadRequested?.Invoke();
    public void OnSavePressed() => RequestSave();
    public void OnBackPressed() => BackRequested?.Invoke();

    public void OnZoomInPressed() => _canvas.ZoomIn();
    public void OnZoomOutPressed() => _canvas.ZoomOut();
    public void OnZoomResetPressed() => _canvas.ResetView();
    public void OnFrameMapPressed() => _canvas.FrameMap();

    public void SelectBrush(MapEditorLayer layer, MapEditorBrushId id)
    {
        _browser.SelectBrush(layer, id);
    }

    public void ConfigureTextureSources(IMapEditorTextureResolver resolver,
        MapEditorTextureSourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(sources);
        _canvas.ConfigureTextureResolver(resolver);
        _stamps.ConfigureTextureResolver(resolver);
        _inspectors.ConfigureTextureResolver(resolver);
        _inspectors.Brush.ConfigureTextureSources(sources);
    }

    public void OnInitializeBrushLayersPressed()
    {
        if (_snapshot?.SourceStatus != MapEditorRasterSourceStatus.OBSOLETE)
            return;
        _confirmingBrushInitialization = true;
        _discardDialog.Title = "Enable layer editing";
        _discardDialog.DialogText =
            "Existing layer images won't become editable. When you save, your new shapes will replace them.";
        _discardDialog.OkButtonText = "Enable editing";
        _discardDialog.PopupCentered();
    }

    public void Apply(MapEditorUpdate update)
    {
        _snapshot = update.Snapshot;
        _topBar.Apply(update.Snapshot);
        _workspaceShell.ApplyProblems(update.Snapshot.Diagnostics);
        _canvas.Apply(update);
        _inspectors.Apply(update.Snapshot);
        _browser.Apply(update.Snapshot);
        RefreshStampLibrary();
        ShowEditDomain(_canvas.EditDomain, false);
        ShowTool(_canvas.Tool, false);
        ShowSnap(_canvas.Snap);
        UpdateWorkspaceStatus();
    }

    public void ShowStatus(MapEditorStatus status)
    {
        _topBar.ShowStatus(status);
        if (!status.IsError)
            return;
        _errorDialog.DialogText = status.Message;
        _errorDialog.PopupCentered();
    }

    public void ShowDiscardConfirmation()
    {
        _confirmingBrushInitialization = false;
        _discardDialog.Title = "Unsaved changes";
        _discardDialog.DialogText =
            "This map has unsaved changes. Discard them and continue?";
        _discardDialog.OkButtonText = "Discard changes";
        _discardDialog.PopupCentered();
    }

    private void RequestSave()
    {
        if (_snapshot?.CanSave == true)
            SaveRequested?.Invoke();
    }

    private void ForwardZoneAdd(MapEditorZoneDraft draft) =>
        ZoneAddRequested?.Invoke(draft);

    private void ForwardZoneReplace(MapEditorZoneId id, MapEditorZoneDraft draft) =>
        ZoneReplaceRequested?.Invoke(id, draft);

    private void ForwardSpawnAdd(MapSpawnPoint spawn) =>
        SpawnAddRequested?.Invoke(spawn);

    private void ForwardSpawnReplace(MapEditorSpawnId id, MapSpawnPoint spawn) =>
        SpawnReplaceRequested?.Invoke(id, spawn);

    private void OnDiscardConfirmed()
    {
        if (_confirmingBrushInitialization)
        {
            _confirmingBrushInitialization = false;
            BrushSourceInitializationRequested?.Invoke();
            return;
        }

        DiscardConfirmed?.Invoke();
    }

    private void OnDiscardCancelled()
    {
        if (_confirmingBrushInitialization)
        {
            _confirmingBrushInitialization = false;
            return;
        }

        DiscardCancelled?.Invoke();
    }

    private void ShowCursorPosition(int x, int y)
    {
        _cursorX = x;
        _cursorY = y;
        UpdateWorkspaceStatus();
    }

    private void ShowZoom(float zoom)
    {
        _zoom = zoom;
        _topBar.ApplyZoom(zoom);
        UpdateWorkspaceStatus();
    }

    private void ConfigureWorkspaceShell()
    {
        ShowSnap(_canvas.Snap);
        UpdateResponsiveControls();
    }

    private void UpdateResponsiveControls()
    {
        bool compact = _workspaceShell.IsCompact;
        _topBar.SetCompact(compact);
        _toolbar.SetCompact(compact);
    }

    private void SelectDomain(MapEditorEditDomain domain)
    {
        DiscardInspectorDraft();
        _canvas.SetEditDomain(domain);
        ShowEditDomain(domain);
        _inspectors.Refresh();
    }

    private void SelectTool(MapEditorTool tool)
    {
        DiscardInspectorDraft();
        _canvas.Tool = tool;
        ShowEditDomain(_canvas.EditDomain);
        ShowTool(_canvas.Tool);
    }

    private void SelectStamp(MapEditorStamp stamp)
    {
        DiscardInspectorDraft();
        _canvas.SelectStamp(stamp);
        ShowEditDomain(_canvas.EditDomain);
        ShowTool(_canvas.Tool);
        RefreshStampLibrary();
    }

    private void OnStampSaveSelected(MapEditorBrushId id) => StampSaveRequested?.Invoke(id);

    private void OnStampRemove(MapEditorStampId id) => StampRemoveRequested?.Invoke(id);

    private void ShowBrushSelection(MapEditorBrushId? id)
    {
        _inspectors.ShowBrushSelection(id);
        RefreshStampLibrary();
    }

    private void RefreshStampLibrary()
    {
        if (_snapshot != null)
            _stamps.Apply(_snapshot, _canvas.SelectedBrushId, _canvas.SelectedStampId);
    }

    private void DiscardInspectorDraft()
    {
        _inspectors.DiscardDraft();
    }

    private void ShowEditDomain(MapEditorEditDomain domain) => ShowEditDomain(domain, true);

    private void ShowEditDomain(MapEditorEditDomain domain, bool refreshBrowser)
    {
        _toolbar.ApplyDomain(domain);
        _workspaceShell.SetStampLibraryAvailable(domain == MapEditorEditDomain.GEOMETRY);
        bool rasterOnly = _snapshot?.SourceStatus == MapEditorRasterSourceStatus.OBSOLETE;
        if (refreshBrowser)
            _browser.ShowDomain(domain, rasterOnly);
        else
            _workspaceShell.ShowObjectBrowserState(domain, rasterOnly);
        UpdateWorkspaceStatus();
    }

    private void SetSnap(MapEditorSnap snap)
    {
        _canvas.Snap = snap;
        ShowSnap(snap);
    }

    private void SetViewVisibility(MapEditorViewLayer layer, bool visible)
    {
        _browser.SetVisibility(layer, visible);
    }

    private void ShowSnap(MapEditorSnap snap)
    {
        _viewControls.ApplySnap(snap);
        UpdateWorkspaceStatus();
    }

    private void ShowTool(MapEditorTool tool) => ShowTool(tool, true);

    private void ShowTool(MapEditorTool tool, bool refreshBrowser)
    {
        _toolbar.ApplyTool(tool);
        RefreshViewVisibility();
        if (refreshBrowser)
        {
            _browser.Refresh();
            RefreshStampLibrary();
        }
        UpdateWorkspaceStatus();
    }

    private void RefreshViewVisibility()
    {
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.BACKGROUND, _canvas.ShowBackground);
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.SOLID, _canvas.ShowSolid);
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.DESTRUCTIBLE, _canvas.ShowDestructible);
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.ZONES, _canvas.ShowZones);
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.SPAWNS, _canvas.ShowSpawns);
        _viewControls.ApplyViewVisibility(MapEditorViewLayer.GRID, _canvas.ShowGrid);
    }

    private void BuildInspectors()
    {
        _inspectors = new MapEditorInspectorPresenter(_canvas, _workspaceShell,
            _effectRowScene, () => _browser.Refresh());
        _inspectors.BrushReplaceRequested += (id, draft) =>
            BrushReplaceRequested?.Invoke(id, draft);
        _inspectors.BrushRemoveRequested += id => BrushRemoveRequested?.Invoke(id);
        _inspectors.BrushDuplicateRequested += (id, offset) =>
            BrushDuplicateRequested?.Invoke(id, offset);
        _inspectors.BrushMoveToLayerRequested += (id, layer) =>
            BrushMoveToLayerRequested?.Invoke(id, layer);
        _inspectors.ZoneReplaceRequested += (id, draft) =>
            ZoneReplaceRequested?.Invoke(id, draft);
        _inspectors.ZoneRemoveRequested += id => ZoneRemoveRequested?.Invoke(id);
        _inspectors.SpawnReplaceRequested += (id, spawn) =>
            SpawnReplaceRequested?.Invoke(id, spawn);
        _inspectors.SpawnRemoveRequested += id => SpawnRemoveRequested?.Invoke(id);
    }

    private void BuildBrowser()
    {
        _browser = new MapEditorBrowserPresenter(_workspaceShell, _canvas, _inspectors);
        _browser.BrushReplaceRequested += (id, draft) =>
            BrushReplaceRequested?.Invoke(id, draft);
        _browser.BrushReorderRequested += (id, destination) =>
            BrushReorderRequested?.Invoke(id, destination);
        _browser.ViewChanged += RefreshViewVisibility;
    }

    bool IMapEditorShortcutTarget.IsTextEditing => IsTextEditing();
    bool IMapEditorShortcutTarget.IsCreatingPolygon => _canvas.IsCreatingPolygon;
    void IMapEditorShortcutTarget.Save() => RequestSave();
    void IMapEditorShortcutTarget.Undo() => UndoRequested?.Invoke();
    void IMapEditorShortcutTarget.Redo() => RedoRequested?.Invoke();
    bool IMapEditorShortcutTarget.DeleteSelection() => DeleteSelection();
    bool IMapEditorShortcutTarget.DuplicateSelection() => DuplicateSelection();

    bool IMapEditorShortcutTarget.SelectDomain(MapEditorEditDomain domain) =>
        SelectShortcutDomain(domain);

    bool IMapEditorShortcutTarget.SelectTool(MapEditorTool tool) => SelectShortcutTool(tool);
    bool IMapEditorShortcutTarget.SelectShape(bool rectangle) => SelectShapeShortcut(rectangle);
    bool IMapEditorShortcutTarget.SelectGeometryTool(MapEditorTool tool) => SelectGeometryTool(tool);
    bool IMapEditorShortcutTarget.SelectSpawnTool() => SelectSpawnTool();

    bool IMapEditorShortcutTarget.FrameAll(bool selectionOnly) =>
        selectionOnly ? _canvas.FrameSelection() : FrameAllShortcut();

    bool IMapEditorShortcutTarget.Cancel()
    {
        if (_workspaceShell.TryCloseProblems() || _workspaceShell.TryCloseStamps() ||
            _workspaceShell.TryCloseProperties() ||
            _workspaceShell.TryCloseDrawer() || _canvas.TryCancelInteraction())
        {
            return true;
        }

        if (_canvas.SelectedBrushId == null && _canvas.SelectedZoneId == null &&
            _canvas.SelectedSpawnId == null && !IsTextEditing())
        {
            return false;
        }

        DiscardInspectorDraft();
        _canvas.SelectBrush(null);
        _canvas.Select(null);
        _canvas.SelectSpawn(null);
        _inspectors.Refresh();
        return true;
    }

    void IMapEditorShortcutTarget.CycleSnap(bool gridOnly)
    {
        if (gridOnly)
        {
            _canvas.ShowGrid = !_canvas.ShowGrid;
        }
        else
        {
            SetSnap(_canvas.Snap switch
            {
                MapEditorSnap.NONE => MapEditorSnap.PIXELS_8,
                MapEditorSnap.PIXELS_8 => MapEditorSnap.PIXELS_16,
                MapEditorSnap.PIXELS_16 => MapEditorSnap.PIXELS_32,
                _ => MapEditorSnap.NONE,
            });
        }

        _canvas.QueueRedraw();
        RefreshViewVisibility();
    }

    void IMapEditorShortcutTarget.CompletePolygon()
    {
        _canvas.TryClosePolygon();
        _inspectors.ShowBrushDiagnostic(_canvas.BrushDiagnostic);
    }

    void IMapEditorShortcutTarget.RemovePolygonVertex() =>
        _canvas.TryRemoveLastPolygonVertex();

    private bool IsTextEditing() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;

    private int DuplicateOffset() => _canvas.Snap == MapEditorSnap.NONE ? 1 : (int)_canvas.Snap;

    private bool DeleteSelection()
    {
        switch (_canvas.EditDomain)
        {
            case MapEditorEditDomain.GEOMETRY when _canvas.SelectedBrushId is { } brush:
                BrushRemoveRequested?.Invoke(brush);
                return true;
            case MapEditorEditDomain.ZONES when _canvas.SelectedZoneId is { } zone:
                ZoneRemoveRequested?.Invoke(zone);
                return true;
            case MapEditorEditDomain.SPAWNS when _canvas.SelectedSpawnId is { } spawn:
                SpawnRemoveRequested?.Invoke(spawn);
                return true;
            default:
                return false;
        }
    }

    private bool DuplicateSelection()
    {
        int offset = DuplicateOffset();
        switch (_canvas.EditDomain)
        {
            case MapEditorEditDomain.GEOMETRY when _canvas.SelectedBrushId is { } brush:
                BrushDuplicateRequested?.Invoke(brush, offset);
                return true;
            case MapEditorEditDomain.ZONES when _canvas.SelectedZoneId is { } zone:
                ZoneDuplicateRequested?.Invoke(zone, offset);
                return true;
            case MapEditorEditDomain.SPAWNS when _canvas.SelectedSpawnId is { } spawn:
                SpawnDuplicateRequested?.Invoke(spawn, offset);
                return true;
            default:
                return false;
        }
    }

    private bool SelectShortcutDomain(MapEditorEditDomain domain)
    {
        SelectDomain(domain);
        SelectTool(MapEditorTool.SELECT);
        return true;
    }

    private bool SelectShortcutTool(MapEditorTool tool)
    {
        SelectTool(tool);
        return true;
    }

    private bool SelectShapeShortcut(bool rectangle)
    {
        return _canvas.EditDomain switch
        {
            MapEditorEditDomain.GEOMETRY => SelectGeometryTool(rectangle
                ? MapEditorTool.BRUSH_RECT
                : MapEditorTool.BRUSH_ELLIPSE),
            MapEditorEditDomain.ZONES => SelectShortcutTool(rectangle
                ? MapEditorTool.RECT
                : MapEditorTool.CIRCLE),
            _ => false,
        };
    }

    private bool SelectGeometryTool(MapEditorTool tool)
    {
        if (_canvas.EditDomain != MapEditorEditDomain.GEOMETRY ||
            _snapshot?.CanEditBrushes != true)
            return false;
        SelectTool(tool);
        return true;
    }

    private bool SelectSpawnTool()
    {
        if (_canvas.EditDomain != MapEditorEditDomain.SPAWNS)
            return false;
        SelectTool(MapEditorTool.SPAWN);
        return true;
    }

    private bool FrameAllShortcut()
    {
        _canvas.FrameMap();
        return true;
    }

    private void UpdateWorkspaceStatus()
    {
        if (!IsInstanceValid(_workspaceShell))
            return;
        string domain = _canvas.EditDomain switch
        {
            MapEditorEditDomain.GEOMETRY => "Geometry",
            MapEditorEditDomain.ZONES => "Zones",
            _ => "Spawns",
        };
        string tool = _canvas.Tool switch
        {
            MapEditorTool.SELECT => "Select",
            MapEditorTool.RECT or MapEditorTool.BRUSH_RECT => "Rectangle",
            MapEditorTool.CIRCLE or MapEditorTool.BRUSH_ELLIPSE => "Ellipse",
            MapEditorTool.BRUSH_POLYGON => "Polygon",
            MapEditorTool.STAMP => "Stamp",
            _ => "Place",
        };
        string snap = _canvas.Snap == MapEditorSnap.NONE ? "Snap 1" : $"Snap {(int)_canvas.Snap}";
        MapEditorMapBounds bounds = _snapshot?.Bounds ?? default;
        _workspaceShell.SetWorkspaceStatus($"{domain} * {tool}",
            $"X {_cursorX} Y {_cursorY}", $"{_zoom * 100:0}% * {snap}",
            $"Map {bounds.X},{bounds.Y} - {bounds.Width}x{bounds.Height}");
    }

    private void ActivateProblem(ContentDiagnostic diagnostic)
    {
        if (_snapshot == null)
            return;
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            if (_snapshot.BrushDocument == null)
                break;
            foreach (MapEditorBrush brush in _snapshot.BrushDocument.Layers.Get(layer).Brushes)
            {
                string prefix = $"Brush {brush.Id.Value} ";
                if (!diagnostic.Message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                SelectDomain(MapEditorEditDomain.GEOMETRY);
                SelectBrush(layer, brush.Id);
                _workspaceShell.TryCloseProblems();
                return;
            }
        }

        if (TryDiagnosticIndex(diagnostic.Message, "zones[", out int zoneIndex) &&
            (uint)zoneIndex < (uint)_snapshot.Zones.Length)
        {
            SelectDomain(MapEditorEditDomain.ZONES);
            _canvas.Select(_snapshot.Zones[zoneIndex].Id);
            _workspaceShell.TryCloseProblems();
            return;
        }

        if (TryDiagnosticIndex(diagnostic.Message, "spawn_points[", out int spawnIndex) &&
            (uint)spawnIndex < (uint)_snapshot.SpawnPoints.Length)
        {
            SelectDomain(MapEditorEditDomain.SPAWNS);
            _canvas.SelectSpawn(_snapshot.SpawnPoints[spawnIndex].Id);
            _workspaceShell.TryCloseProblems();
        }
    }

    private static bool TryDiagnosticIndex(string message, string prefix, out int index)
    {
        index = -1;
        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        int end = message.IndexOf(']', prefix.Length);
        return end > prefix.Length &&
               int.TryParse(message[prefix.Length..end], out index);
    }
}
