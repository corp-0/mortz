using System.Collections.Immutable;
using Godot;
using Mortz.Content;
using SimVec2 = Mortz.Core.Sim.Vec2;

namespace Mortz.Client.MapEditor;

public enum MapEditorTool
{
    SELECT,
    RECT,
    CIRCLE,
    SPAWN,
    BRUSH_RECT,
    BRUSH_ELLIPSE,
    BRUSH_POLYGON,
    STAMP,
}

public partial class MapEditorCanvas(IMapEditorTextureResolver previewResolver) : Control
{
    private const float HANDLE_HIT_RADIUS = 12f;
    private const float DRAG_THRESHOLD = 4f;
    private const float ZOOM_STEP = 1.2f;
    public const int GRID_SIZE = 32;

    [Export] private Texture2D _playerTexture = null!;
    private readonly MapEditorInteraction _interaction = new();
    private MapEditorCanvasResources _resources = new(previewResolver);
    private readonly MapEditorCanvasPicker _picker = new();
    private readonly MapEditorCanvasCamera _camera = new();
    private MapEditorCanvasCursorResolver? _cursorResolver;
    private MapEditorCanvasDraftFactory? _draftFactory;
    private MapEditorCanvasRenderer? _renderer;
    private MapEditorSnapshot? _snapshot;
    private MapEditorBrushDraft? _inspectorBrushPreview;
    private MapEditorZoneDraft? _inspectorZonePreview;
    private MapSpawnPoint? _inspectorSpawnPreview;
    private MapEditorStamp? _selectedStamp;
    private MapEditorBrushDraft? _stampPreview;
    private MapEditorPoint? _lastStampedCell;
    private readonly List<MapEditorBrushDraft> _stampStroke = [];
    private readonly HashSet<string> _stampPlacementNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stampNextName = new(StringComparer.Ordinal);
    private readonly HashSet<MapEditorBrushId> _stampEraseIds = [];
    private MapEditorPoint? _lastErasedCell;
    private bool _stampErasing;
    private Vector2 _cursorMapPosition;
    private bool _panning;
    private bool _spaceHeld;
    private bool _spacePanning;
    private bool _bodyDragPending;
    private bool _pointerInteractionActive;
    private Vector2 _pointerPressLocal;
    private bool _cursorVisible;

    public MapEditorCanvas() : this(new MapEditorTextureResolver())
    {
    }

    public event Action<MapEditorZoneId?>? SelectionChanged;
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
    public event Action<ImmutableArray<MapEditorBrushDraft>>? BrushBatchAddRequested;
    public event Action<ImmutableArray<MapEditorBrushId>>? BrushBatchRemoveRequested;
    public event Action<MapEditorBrushId, MapEditorBrushDraft>? BrushReplaceRequested;
    public event Action<string?>? BrushDiagnosticChanged;
    public event Action<int, int>? CursorMoved;
    public event Action<float>? ZoomChanged;
    public event Action<MapEditorEditDomain>? EditDomainChanged;
    public event Action<MapEditorTool>? ToolChanged;
    public event Action? PointerInteractionFinished;

    public bool PointerInteractionActive => _pointerInteractionActive;

    private MapEditorTool _tool;

    public MapEditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                EnsureToolOverlayVisible(value);
                QueueRedraw();
                return;
            }

            CancelPointerInteraction();
            ResetOverlapCycle();
            MapEditorEditDomain? domain = DomainForTool(value);
            if (domain != null)
                SetEditDomain(domain.Value);
            _tool = value;
            if (value != MapEditorTool.STAMP)
            {
                _selectedStamp = null;
                _lastStampedCell = null;
                _stampPreview = null;
            }
            MouseDefaultCursorShape = value == MapEditorTool.SELECT
                ? CursorShape.Arrow
                : CursorShape.Cross;
            ClearIncompatibleSelection(value);
            EnsureToolOverlayVisible(value);
            ToolChanged?.Invoke(value);
            QueueRedraw();
        }
    }

    public MapEditorZoneId? SelectedZoneId => _interaction.SelectedZoneId;
    public MapEditorSpawnId? SelectedSpawnId => _interaction.SelectedSpawnId;
    public MapEditorBrushId? SelectedBrushId => _interaction.SelectedBrushId;
    public MapEditorStampId? SelectedStampId => _selectedStamp?.Id;
    public bool HasCancellableInteraction => _interaction.Dragging || _interaction.PolygonCreating;
    public bool IsCreatingPolygon => _interaction.PolygonCreating;
    public string? BrushDiagnostic => _interaction.BrushDiagnostic;
    public MapEditorLayer SelectedLayer => _interaction.SelectedLayer;
    public MapEditorEditDomain EditDomain => _interaction.EditDomain;

    public MapEditorSnap Snap
    {
        get => _interaction.Snap;
        set
        {
            _interaction.Snap = value;
            UpdateStampPreview(_cursorMapPosition);
        }
    }

    private bool _showBackground = true;
    private bool _showSolid = true;
    private bool _showDestructible = true;

    public bool ShowBackground
    {
        get => _showBackground;
        set => SetLayerVisibility(ref _showBackground, value);
    }

    public bool ShowSolid
    {
        get => _showSolid;
        set => SetLayerVisibility(ref _showSolid, value);
    }

    public bool ShowDestructible
    {
        get => _showDestructible;
        set => SetLayerVisibility(ref _showDestructible, value);
    }

    private bool _showZones = true;
    private bool _showSpawns = true;

    public bool ShowZones
    {
        get => _showZones;
        set
        {
            _showZones = value;
            QueueRedraw();
        }
    }

    public bool ShowSpawns
    {
        get => _showSpawns;
        set
        {
            _showSpawns = value;
            QueueRedraw();
        }
    }

    public bool ShowBrushOutlines { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    private float Zoom => _camera.Zoom;

    public override void _Ready()
    {
        _interaction.ZoneSelectionChanged += OnZoneSelectionChanged;
        _interaction.SpawnSelectionChanged += OnSpawnSelectionChanged;
        _interaction.ZonePreviewChanged += OnZonePreviewChanged;
        _interaction.SpawnPreviewChanged += OnSpawnPreviewChanged;
        _interaction.ZoneAddRequested += draft => ZoneAddRequested?.Invoke(draft);
        _interaction.ZoneReplaceRequested += (id, draft) => ZoneReplaceRequested?.Invoke(id, draft);
        _interaction.SpawnAddRequested += spawn => SpawnAddRequested?.Invoke(spawn);
        _interaction.SpawnReplaceRequested += (id, spawn) => SpawnReplaceRequested?.Invoke(id, spawn);
        _interaction.LayerSelectionChanged += layer => LayerSelectionChanged?.Invoke(layer);
        _interaction.BrushSelectionChanged += OnBrushSelectionChanged;
        _interaction.BrushPreviewChanged += OnBrushPreviewChanged;
        _interaction.BrushAddRequested += draft => BrushAddRequested?.Invoke(draft);
        _interaction.BrushReplaceRequested += (id, draft) => BrushReplaceRequested?.Invoke(id, draft);
        _interaction.BrushDiagnosticChanged += diagnostic => BrushDiagnosticChanged?.Invoke(diagnostic);
        _interaction.EditDomainChanged += domain => EditDomainChanged?.Invoke(domain);
        Tool = MapEditorTool.SELECT;
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;
        ClipContents = true;
        SetProcessUnhandledInput(true);
        Resized += OnResized;
    }

    public override void _ExitTree()
    {
        Resized -= OnResized;
        Material = null;
        _resources.Dispose();
    }

    public void ConfigureTextureResolver(IMapEditorTextureResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resources.Dispose();
        _resources = new MapEditorCanvasResources(resolver);
        _draftFactory = null;
        _renderer = null;
        QueueRedraw();
    }

    public void Apply(MapEditorUpdate update)
    {
        bool resetView = update.Change is MapEditorOpened or MapEditorReloaded;
        if (resetView)
            _resources.InvalidatePreviews();
        HashSet<MapEditorLayer> changedLayers = MapEditorCanvasResources.ChangedBakedLayers(
            _snapshot, update.Snapshot);
        _snapshot = update.Snapshot;
        _camera.ApplyBounds(update.Snapshot.Bounds, resetView);
        _interaction.Apply(update);
        if (resetView)
        {
            _selectedStamp = null;
            _stampPreview = null;
        }
        else if (_selectedStamp != null)
        {
            _selectedStamp = update.Snapshot.BrushDocument?.Stamps
                .FirstOrDefault(stamp => stamp.Id == _selectedStamp.Id);
            if (_selectedStamp == null)
            {
                _stampPreview = null;
                if (_tool == MapEditorTool.STAMP)
                    Tool = MapEditorTool.SELECT;
            }
            else if (_tool == MapEditorTool.STAMP)
            {
                _interaction.SelectBrush(null);
                UpdateStampPreview(_cursorMapPosition);
            }
        }
        ResetOverlapCycle();
        if (resetView)
        {
            _resources.ClearMaterials();
        }
        else
        {
            RememberChangedMaterial(update);
        }

        if (resetView)
        {
            _tool = MapEditorTool.SELECT;
            MouseDefaultCursorShape = CursorShape.Arrow;
            ToolChanged?.Invoke(_tool);
        }

        if (resetView || update.Change is MapEditorBrushReplaced or
                MapEditorBrushRemoved or MapEditorBrushMovedToLayer)
            _inspectorBrushPreview = null;
        RefreshBakedTextures(changedLayers);
        switch (update.Change)
        {
            case MapEditorZoneReplaced zoneReplaced when
                SelectedZoneId == zoneReplaced.Id:
                SelectionChanged?.Invoke(zoneReplaced.Id);
                break;
            case MapEditorSpawnReplaced spawnReplaced when
                SelectedSpawnId == spawnReplaced.Id:
                SpawnSelectionChanged?.Invoke(spawnReplaced.Id);
                break;
        }

        if (resetView)
        {
            ZoomChanged?.Invoke(Zoom);
        }

        QueueRedraw();
    }

    public void Select(MapEditorZoneId? id)
    {
        _inspectorZonePreview = null;
        _interaction.SelectZone(id);
    }

    public void SelectSpawn(MapEditorSpawnId? id)
    {
        _inspectorSpawnPreview = null;
        _interaction.SelectSpawn(id);
    }

    public void SelectBrush(MapEditorBrushId? id)
    {
        _inspectorBrushPreview = null;
        _interaction.SelectBrush(id);
        if (id != null && SelectedBrush() is { } brush)
            RememberResolvedMaterial(brush);
    }

    public void SelectStamp(MapEditorStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        _selectedStamp = stamp;
        SelectLayer(stamp.Brush.Layer);
        Tool = MapEditorTool.STAMP;
        UpdateStampPreview(_cursorMapPosition);
    }

    public void PreviewSelectedBrush(MapEditorBrushDraft? preview)
    {
        _inspectorBrushPreview = SelectedBrushId == null ? null : preview;
        QueueRedraw();
    }

    public void PreviewSelectedZone(MapEditorZoneDraft? preview)
    {
        _inspectorZonePreview = SelectedZoneId == null ? null : preview;
        QueueRedraw();
    }

    public void PreviewSelectedSpawn(MapSpawnPoint? preview)
    {
        _inspectorSpawnPreview = SelectedSpawnId == null ? null : preview;
        QueueRedraw();
    }

    public void SelectLayer(MapEditorLayer layer)
    {
        ResetOverlapCycle();
        _interaction.SelectLayer(layer);
        RefreshBakedTextures(null);
        QueueRedraw();
    }

    public void SetEditDomain(MapEditorEditDomain domain)
    {
        ResetOverlapCycle();
        _inspectorBrushPreview = null;
        _inspectorZonePreview = null;
        _inspectorSpawnPreview = null;
        _interaction.SetEditDomain(domain);
        if (DomainForTool(_tool) is { } toolDomain && toolDomain != domain)
        {
            _tool = MapEditorTool.SELECT;
            _selectedStamp = null;
            _stampPreview = null;
            ToolChanged?.Invoke(_tool);
        }

        MouseDefaultCursorShape = _tool == MapEditorTool.SELECT
            ? CursorShape.Arrow
            : CursorShape.Cross;
        QueueRedraw();
    }

    public void CancelInteraction()
    {
        CancelPointerInteraction();
        QueueRedraw();
    }

    public bool TryCancelInteraction()
    {
        if (!HasCancellableInteraction)
            return false;
        CancelInteraction();
        return true;
    }

    public bool TryClosePolygon() => _interaction.TryCommitPolygonCreation();

    public bool TryRemoveLastPolygonVertex() => _interaction.RemoveLastPolygonVertex();

    public void ZoomIn() => SetZoom(Zoom * ZOOM_STEP, Size / 2f);
    public void ZoomOut() => SetZoom(Zoom / ZOOM_STEP, Size / 2f);

    public void ResetView()
    {
        if (_camera.Reset())
            ViewChanged();
    }

    public void FrameMap()
    {
        if (_camera.FrameMap(Size))
            ViewChanged();
    }

    public bool FrameSelection()
    {
        if (SelectedBrushId is { } brushId)
        {
            FrameBrush(SelectedLayer, brushId);
            return true;
        }

        if (SelectedZoneId is { } zoneId)
        {
            FrameZone(zoneId);
            return true;
        }

        if (SelectedSpawnId is { } spawnId)
        {
            FrameSpawn(spawnId);
            return true;
        }

        return false;
    }

    public void FrameBrush(MapEditorLayer layer, MapEditorBrushId id)
    {
        MapEditorBrush? brush = _snapshot?.BrushDocument?.Layers.Get(layer).Brushes
            .FirstOrDefault(candidate => candidate.Id == id);
        if (brush != null)
            FrameBounds(MapEditorGeometry.Bounds(brush.Shape));
    }

    public void FrameZone(MapEditorZoneId id)
    {
        MapEditorZone? zone = _snapshot?.Zones.FirstOrDefault(candidate => candidate.Id == id);
        if (zone != null)
            FrameBounds(ZoneBounds(zone.Shape));
    }

    public void FrameSpawn(MapEditorSpawnId id)
    {
        if (_snapshot == null)
            return;
        MapEditorSpawn? spawn = _snapshot.SpawnPoints.FirstOrDefault(candidate => candidate.Id == id);
        if (spawn != null)
            FrameBounds(new MapEditorBounds(spawn.Value.Value.X - 16, spawn.Value.Value.Y - 16,
                spawn.Value.Value.X + 16, spawn.Value.Value.Y + 16));
    }

    private void FrameBounds(MapEditorBounds bounds)
    {
        if (_camera.Frame(bounds, Size))
            ViewChanged();
    }

    private static MapEditorBounds ZoneBounds(MapZoneShape shape) => shape switch
    {
        RectMapZoneShape rect => MapEditorGeometry.Bounds(new MapEditorRectBrushShape(
            rect.X, rect.Y, rect.Width, rect.Height, rect.Rotation)),
        CircleMapZoneShape circle => new MapEditorBounds(circle.X - circle.Radius,
            circle.Y - circle.Radius, circle.X + circle.Radius, circle.Y + circle.Radius),
        EllipseMapZoneShape ellipse => MapEditorGeometry.Bounds(new MapEditorEllipseBrushShape(
            ellipse.X, ellipse.Y, ellipse.RadiusX, ellipse.RadiusY, ellipse.Rotation)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private void ViewChanged()
    {
        ZoomChanged?.Invoke(Zoom);
        QueueRedraw();
    }

    public override void _Draw()
    {
        _renderer ??= new MapEditorCanvasRenderer(this, _resources);
        _renderer.Draw(new MapEditorCanvasRenderFrame(
            _snapshot, _camera, _playerTexture, SelectedLayer, EditDomain, Tool,
            SelectedZoneId, SelectedSpawnId, SelectedBrushId,
            _interaction.ZonePreview, _interaction.SpawnPreview, _interaction.BrushPreview,
            _stampStroke, _stampEraseIds,
            _inspectorZonePreview, _inspectorSpawnPreview,
            _stampPreview ?? _inspectorBrushPreview,
            _interaction.PolygonCreating, ShowBackground, ShowSolid, ShowDestructible,
            ShowZones, ShowSpawns, ShowGrid, ShowBrushOutlines, _cursorVisible,
            _cursorMapPosition));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_snapshot == null || _camera.Bounds.Width <= 0 || _camera.Bounds.Height <= 0)
            return;
        switch (@event)
        {
            case InputEventMouseButton
            {
                Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown
            } wheel:
                {
                    float factor = wheel.ButtonIndex == MouseButton.WheelUp ? ZOOM_STEP : 1f / ZOOM_STEP;
                    SetZoom(Zoom * factor, wheel.Position);
                    AcceptEvent();
                    return;
                }
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed;
                MouseDefaultCursorShape = middle.Pressed ? CursorShape.Drag : CursorShape.Arrow;
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                {
                    if (_spaceHeld)
                    {
                        _spacePanning = button.Pressed;
                        MouseDefaultCursorShape = button.Pressed ? CursorShape.Drag : CursorShape.Arrow;
                        AcceptEvent();
                        return;
                    }

                    if (button.Pressed)
                    {
                        _pointerInteractionActive = true;
                        _pointerPressLocal = button.Position;
                        BeginDrag(LocalToMap(button.Position), button.AltPressed);
                    }
                    else if (_interaction.Dragging)
                    {
                        if (_bodyDragPending)
                        {
                            _bodyDragPending = false;
                            CancelPointerInteraction();
                        }
                        else
                            EndDrag(LocalToMap(button.Position));
                    }
                    else if (!button.Pressed && Tool == MapEditorTool.STAMP)
                    {
                        CommitStampStroke();
                    }

                    if (!button.Pressed)
                    {
                        FinishPointerInteraction();
                    }

                    AcceptEvent();
                    return;
                }
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                if (Tool == MapEditorTool.STAMP)
                {
                    if (right.Pressed)
                    {
                        _pointerInteractionActive = true;
                        _stampErasing = true;
                        _stampEraseIds.Clear();
                        _lastErasedCell = null;
                        EraseStampAt(LocalToMap(right.Position));
                    }
                    else
                    {
                        CommitStampErase();
                        FinishPointerInteraction();
                    }
                    AcceptEvent();
                }
                else if (right.Pressed && EditDomain == MapEditorEditDomain.GEOMETRY &&
                         TryRemovePolygonVertex(LocalToMap(right.Position)))
                {
                    AcceptEvent();
                }
                return;
            case InputEventMouseMotion motion when _panning || _spacePanning:
                ReportCursor(motion.Position);
                MoveCamera(-motion.Relative);
                AcceptEvent();
                return;
            case InputEventMouseMotion stampMotion when
                _pointerInteractionActive && Tool == MapEditorTool.STAMP && !_stampErasing:
                ReportCursor(stampMotion.Position);
                PaintStamp(LocalToMap(stampMotion.Position));
                AcceptEvent();
                return;
            case InputEventMouseMotion eraseMotion when
                _pointerInteractionActive && Tool == MapEditorTool.STAMP && _stampErasing:
                ReportCursor(eraseMotion.Position);
                EraseStampAt(LocalToMap(eraseMotion.Position));
                AcceptEvent();
                return;
            case InputEventMouseMotion dragMotion when _interaction.Dragging:
                ReportCursor(dragMotion.Position);
                if (_bodyDragPending && dragMotion.Position.DistanceTo(_pointerPressLocal) <
                    DRAG_THRESHOLD)
                    return;
                _bodyDragPending = false;
                UpdateDrag(LocalToMap(dragMotion.Position));
                AcceptEvent();
                return;
            case InputEventMouseMotion hoverMotion:
                _picker.ResetIfPointerMoved(hoverMotion.Position);
                ReportCursor(hoverMotion.Position);
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_snapshot == null)
            return;
        if (@event is InputEventKey { Keycode: Key.Space, Echo: false } space)
        {
            _spaceHeld = space.Pressed;
            if (!space.Pressed)
                _spacePanning = false;
            MouseDefaultCursorShape = space.Pressed
                ? CursorShape.PointingHand
                : (_tool == MapEditorTool.SELECT ? CursorShape.Arrow : CursorShape.Cross);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion motion && (_panning || _spacePanning))
        {
            ReportCursor(GetLocalMousePosition());
            MoveCamera(-motion.Relative);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion && _interaction.Dragging)
        {
            ReportCursor(GetLocalMousePosition());
            if (_bodyDragPending && GetLocalMousePosition().DistanceTo(_pointerPressLocal) <
                DRAG_THRESHOLD)
                return;
            _bodyDragPending = false;
            UpdateDrag(LocalToMap(GetLocalMousePosition()));
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Left,
            Pressed: false,
        } && _pointerInteractionActive)
        {
            if (_interaction.Dragging && _bodyDragPending)
            {
                _bodyDragPending = false;
                CancelPointerInteraction();
            }
            else if (_interaction.Dragging)
            {
                EndDrag(LocalToMap(GetLocalMousePosition()));
            }
            else if (Tool == MapEditorTool.STAMP)
            {
                CommitStampStroke();
            }

            FinishPointerInteraction();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Middle,
            Pressed: false,
        })
        {
            _panning = false;
        }
        else if (@event is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Left,
            Pressed: false,
        } && _spacePanning)
        {
            _spacePanning = false;
        }
        else if (@event is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Right,
            Pressed: false,
        } && _pointerInteractionActive && _stampErasing)
        {
            CommitStampErase();
            FinishPointerInteraction();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BeginDrag(Vector2 point, bool cycleOverlap)
    {
        EnsureToolOverlayVisible(Tool);
        if (!cycleOverlap)
            ResetOverlapCycle();
        switch (Tool)
        {
            case MapEditorTool.STAMP:
                if (_lastStampedCell == null)
                    BeginStampStroke();
                PaintStamp(point);
                return;
            case MapEditorTool.SPAWN:
                {
                    MapEditorSpawn? hit = ShowSpawns ? PickSpawn(point, cycleOverlap) : null;
                    if (hit == null)
                    {
                        MapEditorPoint snapped = MapEditorGeometry.Snap(MapPoint(point), Snap);
                        MapSpawnPoint spawn = new(snapped.X, snapped.Y);
                        _interaction.BeginSpawnCreation(spawn, Sim(point));
                    }
                    else
                    {
                        _interaction.BeginSpawnMove(hit.Value.Id, Sim(point));
                        _bodyDragPending = true;
                    }

                    return;
                }
            case MapEditorTool.SELECT:
                {
                    if (EditDomain == MapEditorEditDomain.GEOMETRY)
                    {
                        MapEditorBrush? selectedBrush = SelectedBrush();
                        if (selectedBrush?.Shape is MapEditorRectBrushShape selectedRect)
                        {
                            MapEditorRectHandle handle = MapEditorGeometry.PickRectBrushHandle(
                                selectedRect, MapPoint(point), HANDLE_HIT_RADIUS / Zoom);
                            if (handle is not (MapEditorRectHandle.NONE or MapEditorRectHandle.MOVE) &&
                                _interaction.BeginBrushDrag(selectedBrush.Id, Draft(selectedBrush),
                                    handle, MapPoint(point)))
                                return;
                        }

                        if (selectedBrush?.Shape is MapEditorEllipseBrushShape selectedEllipse)
                        {
                            MapEditorEllipseHandle handle = MapEditorGeometry.PickEllipseBrushHandle(
                                selectedEllipse, MapPoint(point), HANDLE_HIT_RADIUS / Zoom);
                            if (handle is not (MapEditorEllipseHandle.NONE or
                                    MapEditorEllipseHandle.MOVE) &&
                                _interaction.BeginBrushDrag(selectedBrush.Id, Draft(selectedBrush),
                                    handle, MapPoint(point)))
                                return;
                        }

                        if (selectedBrush?.Shape is MapEditorPolygonBrushShape selectedPolygon)
                        {
                            int vertex = MapEditorGeometry.PickPolygonVertex(selectedPolygon,
                                MapPoint(point), HANDLE_HIT_RADIUS / Zoom);
                            if (vertex >= 0 && _interaction.BeginPolygonVertexDrag(selectedBrush.Id,
                                    vertex, MapPoint(point)))
                                return;
                            int edge = MapEditorGeometry.PickPolygonEdge(selectedPolygon,
                                MapPoint(point), HANDLE_HIT_RADIUS / Zoom);
                            if (edge >= 0 && _interaction.BeginPolygonEdgeInsertion(selectedBrush.Id,
                                    edge, MapPoint(point)))
                                return;
                        }

                        MapEditorBrush? brush = PickBrush(point, cycleOverlap);
                        if (brush != null)
                        {
                            SelectBrush(brush.Id);
                            BeginBrushMove(brush, MapPoint(point));
                            _bodyDragPending = true;
                            return;
                        }

                        SelectBrush(null);
                        return;
                    }

                    if (EditDomain == MapEditorEditDomain.SPAWNS)
                    {
                        MapEditorSpawn? spawn = ShowSpawns ? PickSpawn(point, cycleOverlap) : null;
                        if (spawn != null)
                        {
                            SelectSpawn(spawn.Value.Id);
                            _interaction.BeginSpawnMove(spawn.Value.Id, Sim(point));
                            _bodyDragPending = true;
                            return;
                        }

                        SelectSpawn(null);
                        return;
                    }

                    if (!ShowZones)
                    {
                        Select(null);
                        return;
                    }

                    MapEditorZone? selected = SelectedZone();
                    if (selected != null && TryStartScaleHandleDrag(selected, point))
                        return;
                    MapEditorZone? hit = PickZone(point, cycleOverlap);
                    Select(hit?.Id);
                    if (hit == null)
                        return;
                    _interaction.BeginZoneDrag(hit.Id, MapEditorZoneDrag.MOVE, Sim(point));
                    _bodyDragPending = true;
                    return;
                }
            case MapEditorTool.BRUSH_RECT:
                {
                    if (_snapshot?.CanEditBrushes == true)
                    {
                        MapEditorPoint anchor = MapEditorGeometry.Snap(MapPoint(point), Snap);
                        MapEditorRectBrushShape rect = new(anchor.X, anchor.Y,
                            Snap == MapEditorSnap.NONE ? 1 : (int)Snap,
                            Snap == MapEditorSnap.NONE ? 1 : (int)Snap, 0);
                        MapEditorBrushDraft brushDraft = NewBrushDraft(UniqueBrushName(), rect,
                            anchor);
                        _interaction.BeginBrushCreation(brushDraft, anchor);
                    }

                    return;
                }
            case MapEditorTool.BRUSH_ELLIPSE:
                {
                    if (_snapshot?.CanEditBrushes == true)
                    {
                        MapEditorPoint anchor = MapEditorGeometry.Snap(MapPoint(point), Snap);
                        int radius = Snap == MapEditorSnap.NONE ? 1 : (int)Snap;
                        MapEditorEllipseBrushShape ellipse = new(anchor.X, anchor.Y,
                            radius, radius, 0);
                        MapEditorBrushDraft brushDraft = NewBrushDraft(
                            UniqueBrushName("ellipse"), ellipse, anchor);
                        _interaction.BeginBrushCreation(brushDraft, anchor);
                    }

                    return;
                }
            case MapEditorTool.BRUSH_POLYGON:
                {
                    if (_snapshot?.CanEditBrushes != true)
                        return;
                    MapEditorPoint vertex = MapEditorGeometry.Snap(MapPoint(point), Snap);
                    if (!_interaction.PolygonCreating)
                    {
                        MapEditorPolygonBrushShape polygon = new([]);
                        _interaction.BeginPolygonCreation(NewBrushDraft(
                            UniqueBrushName("polygon"), polygon, vertex));
                    }
                    else if (_interaction.BrushPreview?.Shape is MapEditorPolygonBrushShape pending &&
                             pending.Vertices.Length >= 3 &&
                             Distance(vertex, pending.Vertices[0]) <= HANDLE_HIT_RADIUS / Zoom)
                    {
                        _interaction.TryCommitPolygonCreation();
                        return;
                    }

                    _interaction.AppendPolygonVertex(vertex);
                    QueueRedraw();
                    return;
                }
        }

        string name = UniqueName();
        MapZoneShape shape = Tool == MapEditorTool.RECT
            ? new RectMapZoneShape((int)point.X, (int)point.Y, 1, 1)
            : new EllipseMapZoneShape((int)point.X, (int)point.Y, 1, 1);
        MapEditorZoneDraft draft = new(name, [], shape, []);
        _interaction.BeginZoneCreation(
            Tool == MapEditorTool.RECT ? MapEditorZoneDrag.CREATE_RECT : MapEditorZoneDrag.CREATE_CIRCLE,
            draft, Sim(point));
    }

    private bool TryStartScaleHandleDrag(MapEditorZone zone, Vector2 point)
    {
        MapZoneHandle handle = MapEditorGeometry.PickHandle(zone.Shape,
            Sim(point), HANDLE_HIT_RADIUS / Zoom, out SimVec2 anchor);
        if (handle != MapZoneHandle.SCALE)
            return false;
        return _interaction.BeginZoneDrag(zone.Id,
            MapEditorZoneDrag.SCALE, Sim(point), anchor);
    }

    private void UpdateDrag(Vector2 point)
    {
        _interaction.Update(Sim(point));
        QueueRedraw();
    }

    private void EndDrag(Vector2 point)
    {
        _interaction.Commit(Sim(point));
        QueueRedraw();
    }

    private void RefreshBakedTextures(IReadOnlySet<MapEditorLayer>? changedLayers)
    {
        if (_snapshot != null)
        {
            _resources.RefreshBakedTextures(_snapshot, SelectedLayer, ShowBackground, ShowSolid,
                ShowDestructible, changedLayers);
        }
    }

    private void SetLayerVisibility(ref bool field, bool value)
    {
        if (field == value)
            return;
        field = value;
        RefreshBakedTextures(null);
        QueueRedraw();
    }

    private static MapEditorBrush BrushFromDraft(MapEditorBrushId id,
        MapEditorBrushDraft draft) => new(id, draft.Name, draft.Layer, draft.Shape,
        draft.Material, draft.Projection, draft.Visible);

    private bool IsSelectedLayerVisible() => SelectedLayer switch
    {
        MapEditorLayer.BACKGROUND => ShowBackground,
        MapEditorLayer.SOLID => ShowSolid,
        MapEditorLayer.DESTRUCTIBLE => ShowDestructible,
        _ => false,
    };

    private Vector2 LocalToMap(Vector2 point) => _camera.LocalToMap(point, Size);

    private Vector2 MapToLocal(Vector2 point) => _camera.MapToLocal(point, Size);

    private static SimVec2 Sim(Vector2 point) => new(point.X, point.Y);

    private static MapEditorPoint MapPoint(Vector2 point) => new(
        (int)MathF.Round(point.X), (int)MathF.Round(point.Y));

    private void MoveCamera(Vector2 delta)
    {
        ResetOverlapCycle();
        _camera.Move(delta);
        QueueRedraw();
    }

    private void OnResized()
    {
        QueueRedraw();
    }

    private void CancelPointerInteraction()
    {
        _interaction.Cancel();
        _stampStroke.Clear();
        _stampPlacementNames.Clear();
        _stampNextName.Clear();
        _stampEraseIds.Clear();
        _lastErasedCell = null;
        _stampErasing = false;
        _lastStampedCell = null;
        FinishPointerInteraction();
        QueueRedraw();
    }

    private void FinishPointerInteraction()
    {
        if (!_pointerInteractionActive)
        {
            return;
        }

        _pointerInteractionActive = false;
        _lastStampedCell = null;
        PointerInteractionFinished?.Invoke();
    }

    private void PaintStamp(Vector2 point)
    {
        if (_selectedStamp == null)
            return;
        MapEditorPoint current = MapEditorStampGeometry.SnapToCell(MapPoint(point), Snap);
        MapEditorPoint start = _lastStampedCell ?? current;
        bool skipFirst = _lastStampedCell != null;
        _lastStampedCell = current;
        foreach (MapEditorPoint cell in MapEditorStampGeometry.CellsAlongStroke(start, current,
                     Snap))
        {
            if (skipFirst)
            {
                skipFirst = false;
                continue;
            }
            MapEditorStamp stamp = _selectedStamp;
            _stampStroke.Add(MapEditorStampGeometry.Place(stamp, cell,
                UniqueStampPlacementName(stamp.Name)));
            if (_selectedStamp == null || Tool != MapEditorTool.STAMP)
                return;
        }
        QueueRedraw();
    }

    private void CommitStampStroke()
    {
        if (_stampStroke.Count == 0)
            return;
        ImmutableArray<MapEditorBrushDraft> stroke = [.. _stampStroke];
        _stampStroke.Clear();
        _stampPlacementNames.Clear();
        _stampNextName.Clear();
        _lastStampedCell = null;
        BrushBatchAddRequested?.Invoke(stroke);
        QueueRedraw();
    }

    private void EraseStampAt(Vector2 point)
    {
        MapEditorPoint cell = MapEditorStampGeometry.SnapToCell(MapPoint(point), Snap);
        if (_lastErasedCell == cell)
            return;
        _lastErasedCell = cell;
        MapEditorBrush? brush = PickBrush(point, false, _stampEraseIds);
        if (brush == null || !_stampEraseIds.Add(brush.Id))
            return;
        QueueRedraw();
    }

    private void CommitStampErase()
    {
        _stampErasing = false;
        _lastErasedCell = null;
        if (_stampEraseIds.Count == 0)
            return;
        ImmutableArray<MapEditorBrushId> ids = [.. _stampEraseIds];
        _stampEraseIds.Clear();
        BrushBatchRemoveRequested?.Invoke(ids);
        QueueRedraw();
    }

    private void SetZoom(float zoom, Vector2 anchor)
    {
        _camera.SetZoom(zoom, anchor, Size);
        ViewChanged();
    }

    private void ReportCursor(Vector2 localPosition)
    {
        Vector2 point = LocalToMap(localPosition);
        _cursorMapPosition = point;
        _cursorVisible = true;
        UpdateStampPreview(point);
        CursorMoved?.Invoke((int)point.X, (int)point.Y);
        QueueRedraw();
        if (_snapshot == null)
            return;
        _cursorResolver ??= new MapEditorCanvasCursorResolver(_picker);
        MouseDefaultCursorShape = _cursorResolver.Resolve(_snapshot, point, Zoom, Tool,
            EditDomain, SelectedLayer, SelectedBrushId, DisplayedBrushPreview(),
            SelectedBrush(), SelectedZone(), IsSelectedLayerVisible(), ShowSpawns,
            _pointerPressLocal, MouseDefaultCursorShape);
    }

    private void RememberChangedMaterial(MapEditorUpdate update)
    {
        MapEditorBrushId? id = update.Change switch
        {
            MapEditorBrushAdded added => added.Id,
            MapEditorBrushesAdded { Ids.IsEmpty: false } added => added.Ids[^1],
            MapEditorBrushReplaced replaced => replaced.Id,
            MapEditorBrushMovedToLayer moved => moved.Id,
            _ => null,
        };
        if (id == null || update.Snapshot.BrushDocument == null)
            return;
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorBrush? brush = update.Snapshot.BrushDocument.Layers.Get(layer).Brushes
                .FirstOrDefault(candidate => candidate.Id == id);
            if (brush != null)
            {
                RememberResolvedMaterial(brush);
                return;
            }
        }
    }

    private void RememberResolvedMaterial(MapEditorBrush brush)
    {
        _resources.RememberMaterial(brush);
    }

    private string UniqueName() => DraftFactory.UniqueZoneName(_snapshot!);

    private string UniqueBrushName(string shape = "rectangle") =>
        DraftFactory.UniqueBrushName(_snapshot, SelectedLayer, shape);

    private MapEditorBrushDraft NewBrushDraft(string name, MapEditorBrushShape shape,
        MapEditorPoint anchor)
    {
        return DraftFactory.CreateBrush(name, SelectedLayer, shape, anchor);
    }

    private void BeginBrushMove(MapEditorBrush brush, MapEditorPoint point)
    {
        _interaction.BeginBrushMove(brush.Id, Draft(brush), point);
    }

    private bool TryRemovePolygonVertex(Vector2 point)
    {
        MapEditorBrush? brush = SelectedBrush();
        if (brush?.Shape is not MapEditorPolygonBrushShape polygon)
            return false;
        int vertex = MapEditorGeometry.PickPolygonVertex(polygon, MapPoint(point),
            HANDLE_HIT_RADIUS / Zoom);
        return vertex >= 0 && _interaction.RemovePolygonVertex(brush.Id, vertex);
    }

    private static double Distance(MapEditorPoint a, MapEditorPoint b)
    {
        double dx = (long)a.X - b.X;
        double dy = (long)a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private MapEditorBrush? SelectedBrush()
    {
        if (SelectedBrushId is not { } id)
            return null;
        MapEditorBrush? brush = _snapshot?.BrushDocument?.Layers.Get(SelectedLayer).Brushes
            .FirstOrDefault(candidate => candidate.Id == id);
        brush = brush != null && _inspectorBrushPreview is { } preview
            ? BrushFromDraft(id, preview)
            : brush;
        return brush?.Visible == true ? brush : null;
    }

    private MapEditorBrushDraft? DisplayedBrushPreview() =>
        _interaction.BrushPreview ?? _inspectorBrushPreview;

    private MapEditorBrush? PickBrush(Vector2 point, bool cycle,
        IReadOnlySet<MapEditorBrushId>? excluded = null)
        => EditDomain == MapEditorEditDomain.GEOMETRY
            ? _picker.PickBrush(_snapshot, SelectedLayer, SelectedBrushId,
                DisplayedBrushPreview(), point, Zoom, IsSelectedLayerVisible(), cycle,
                _pointerPressLocal, excluded)
            : null;

    private MapEditorZone? SelectedZone() => SelectedZoneId is { } id
        ? _snapshot?.Zones.FirstOrDefault(zone => zone.Id == id)
        : null;

    private MapEditorZone? PickZone(Vector2 point, bool cycle)
        => EditDomain == MapEditorEditDomain.ZONES && ShowZones
            ? _picker.PickZone(_snapshot, point, Zoom, cycle, _pointerPressLocal)
            : null;

    private MapEditorSpawn? PickSpawn(Vector2 point, bool cycle)
        => EditDomain == MapEditorEditDomain.SPAWNS && ShowSpawns
            ? _picker.PickSpawn(_snapshot, point, Zoom, cycle, _pointerPressLocal)
            : null;

    private void ResetOverlapCycle()
    {
        _picker.Reset();
    }

    private static MapEditorEditDomain? DomainForTool(MapEditorTool tool) => tool switch
    {
        MapEditorTool.RECT or MapEditorTool.CIRCLE => MapEditorEditDomain.ZONES,
        MapEditorTool.SPAWN => MapEditorEditDomain.SPAWNS,
        MapEditorTool.BRUSH_RECT or MapEditorTool.BRUSH_ELLIPSE or
            MapEditorTool.BRUSH_POLYGON or MapEditorTool.STAMP => MapEditorEditDomain.GEOMETRY,
        _ => null,
    };

    private void ClearIncompatibleSelection(MapEditorTool tool)
    {
        if (tool != MapEditorTool.SELECT)
        {
            Select(null);
            SelectSpawn(null);
            SelectBrush(null);
        }
    }

    private void EnsureToolOverlayVisible(MapEditorTool tool)
    {
        switch (tool)
        {
            case MapEditorTool.RECT or MapEditorTool.CIRCLE:
                ShowZones = true;
                break;
            case MapEditorTool.SPAWN:
                ShowSpawns = true;
                break;
            case MapEditorTool.BRUSH_RECT or MapEditorTool.BRUSH_ELLIPSE or
                MapEditorTool.BRUSH_POLYGON or MapEditorTool.STAMP:
                SetSelectedLayerVisible(true);
                break;
        }
    }

    private void SetSelectedLayerVisible(bool visible)
    {
        switch (SelectedLayer)
        {
            case MapEditorLayer.BACKGROUND:
                ShowBackground = visible;
                break;
            case MapEditorLayer.SOLID:
                ShowSolid = visible;
                break;
            case MapEditorLayer.DESTRUCTIBLE:
                ShowDestructible = visible;
                break;
        }
    }

    private void OnZoneSelectionChanged(MapEditorZoneId? id)
    {
        SelectionChanged?.Invoke(id);
        QueueRedraw();
    }

    private void OnSpawnSelectionChanged(MapEditorSpawnId? id)
    {
        SpawnSelectionChanged?.Invoke(id);
        QueueRedraw();
    }

    private void OnZonePreviewChanged(MapEditorZoneDraft? preview)
    {
        ZonePreviewChanged?.Invoke(preview);
        QueueRedraw();
    }

    private void OnSpawnPreviewChanged(MapSpawnPoint? preview)
    {
        SpawnPreviewChanged?.Invoke(preview);
        QueueRedraw();
    }

    private void OnBrushSelectionChanged(MapEditorBrushId? id)
    {
        _inspectorBrushPreview = null;
        BrushSelectionChanged?.Invoke(id);
        QueueRedraw();
    }

    private void OnBrushPreviewChanged(MapEditorBrushDraft? preview)
    {
        BrushPreviewChanged?.Invoke(preview);
        QueueRedraw();
    }

    private static MapEditorBrushDraft Draft(MapEditorBrush brush) => new(
        brush.Name, brush.Layer, brush.Shape, brush.Material, brush.Projection, brush.Visible);

    private void UpdateStampPreview(Vector2 point)
    {
        if (_tool != MapEditorTool.STAMP || _selectedStamp == null || !_cursorVisible)
        {
            _stampPreview = null;
            return;
        }
        MapEditorPoint snapped = MapEditorStampGeometry.SnapToCell(MapPoint(point), Snap);
        _stampPreview = MapEditorStampGeometry.Place(_selectedStamp, snapped,
            _selectedStamp.Name);
        QueueRedraw();
    }

    private string UniqueStampPlacementName(string name)
    {
        if (_stampPlacementNames.Add(name))
            return name;
        int number = _stampNextName.GetValueOrDefault(name, 2);
        while (true)
        {
            string candidate = $"{name} {number}";
            number++;
            if (_stampPlacementNames.Add(candidate))
            {
                _stampNextName[name] = number;
                return candidate;
            }
        }
    }

    private void BeginStampStroke()
    {
        _stampStroke.Clear();
        _stampPlacementNames.Clear();
        _stampNextName.Clear();
        if (_snapshot?.BrushDocument == null)
            return;
        _stampPlacementNames.UnionWith(_snapshot.BrushDocument.Layers.Get(SelectedLayer).Brushes
            .Select(brush => brush.Name));
    }

    private MapEditorCanvasDraftFactory DraftFactory =>
        _draftFactory ??= new MapEditorCanvasDraftFactory(_resources);
}
