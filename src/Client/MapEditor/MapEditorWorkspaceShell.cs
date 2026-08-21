using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public enum MapEditorInspectorKind
{
    EMPTY,
    BRUSH,
    ZONE,
    SPAWN,
}

[GlobalClass]
public partial class MapEditorWorkspaceShell : Control
{
    public const float INLINE_DOCK_WIDTH = 340f;
    public const float MIN_INLINE_DOCK_WIDTH = 300f;
    public const float MAX_INLINE_DOCK_WIDTH = 440f;
    public const float COMPACT_WIDTH = 1000f;
    public const float STAMP_DOCK_HEIGHT = 184f;

    [Export] private VBoxContainer _chrome = null!;
    [Export] private HBoxContainer _toolRow = null!;
    [Export] private Control _canvasHost = null!;
    [Export] private MapEditorCanvas _canvas = null!;
    [Export] private PanelContainer _objectDock = null!;
    [Export] private ScrollContainer _objectBrowserScroll = null!;
    [Export] private MapEditorObjectBrowser _objectBrowser = null!;
    [Export] private PanelContainer _inspectorFrame = null!;
    [Export] private Control _inspectorStack = null!;
    [Export] private MapEditorBrushInspector _brushInspector = null!;
    [Export] private MapEditorZoneInspector _zoneInspector = null!;
    [Export] private MapEditorSpawnInspector _spawnInspector = null!;
    [Export] private MapEditorToolbar _toolbar = null!;
    [Export] private MapEditorViewControls _viewControls = null!;
    [Export] private PanelContainer _propertiesDock = null!;
    [Export] private PanelContainer _statusBar = null!;
    [Export] private HBoxContainer _statusRow = null!;
    [Export] private Label _modeStatus = null!;
    [Export] private Label _viewStatus = null!;
    [Export] private Label _boundsStatus = null!;
    [Export] private Button _problemsButton = null!;
    [Export] private PanelContainer _problemsDrawer = null!;
    [Export] private VBoxContainer _problemsList = null!;
    [Export] private Button _problemsClose = null!;
    [Export] private Button _drawerOpen = null!;
    [Export] private Button _drawerClose = null!;
    [Export] private Button _propertiesOpen = null!;
    [Export] private Button _stampOpen = null!;
    [Export] private Button _propertiesClose = null!;
    [Export] private Button _dockResize = null!;
    [Export] private Button _propertiesResize = null!;
    [Export] private PackedScene _stampDockScene = null!;
    private MapEditorStampDock _stampDock = null!;
    private MapEditorInspectorKind _inspectorKind;
    private Control? _focusBeforeDrawer;
    private bool _compact;
    private bool _drawerOpenState = true;
    private bool _propertiesOpenState = true;
    private bool _resizingDock;
    private bool _resizingProperties;
    private bool _problemsOpenState;
    private bool _stampOpenState;
    private bool _stampAvailable;
    private bool _inspectorLayoutPending;
    private float _inlineDockWidth = INLINE_DOCK_WIDTH;
    private float _propertiesDockWidth = INLINE_DOCK_WIDTH;
    private float _wideObjectDockLeft;
    private float _widePropertiesDockRight;
    private float _dockResizeHalfWidth;
    private float _propertiesResizeHalfWidth;
    public MapEditorCanvas Canvas => _canvas;
    public MapEditorBrushInspector BrushInspector => _brushInspector;
    public MapEditorZoneInspector ZoneInspector => _zoneInspector;
    public MapEditorSpawnInspector SpawnInspector => _spawnInspector;
    public MapEditorToolbar Toolbar => _toolbar;
    public MapEditorViewControls ViewControls => _viewControls;
    public MapEditorObjectBrowser ObjectBrowser => _objectBrowser;
    public MapEditorStampLibrary StampLibrary => _stampDock.Library;
    public bool IsCompact => _compact;
    public bool IsStampLibraryOpen => _stampOpenState;

    public event Action? BrushInitializationRequested;
    public event Action<ContentDiagnostic>? ProblemActivated;

    public override void _Ready()
    {
        CaptureSceneLayout();
        _stampDock = _stampDockScene.Instantiate<MapEditorStampDock>();
        _stampDock.Visible = false;
        AddChild(_stampDock);
        _drawerOpen.Pressed += ToggleDrawer;
        _drawerClose.Pressed += CloseDrawer;
        _propertiesOpen.Pressed += ToggleProperties;
        _stampOpen.Pressed += ToggleStamps;
        _propertiesClose.Pressed += CloseProperties;
        _problemsButton.Pressed += ToggleProblems;
        _problemsClose.Pressed += CloseProblems;
        _dockResize.GuiInput += ResizeDock;
        _propertiesResize.GuiInput += ResizeProperties;
        _objectBrowser.BrushInitializationRequested += RequestBrushInitialization;
        _canvas.PointerInteractionFinished += ApplyPendingInspectorLayout;
        Resized += ApplyResponsiveLayout;
        ShowInspector(MapEditorInspectorKind.EMPTY);
        ApplyResponsiveLayout();
    }

    public override void _ExitTree()
    {
        _drawerOpen.Pressed -= ToggleDrawer;
        _drawerClose.Pressed -= CloseDrawer;
        _propertiesOpen.Pressed -= ToggleProperties;
        _stampOpen.Pressed -= ToggleStamps;
        _propertiesClose.Pressed -= CloseProperties;
        _problemsButton.Pressed -= ToggleProblems;
        _problemsClose.Pressed -= CloseProblems;
        _dockResize.GuiInput -= ResizeDock;
        _propertiesResize.GuiInput -= ResizeProperties;
        _objectBrowser.BrushInitializationRequested -= RequestBrushInitialization;
        _canvas.PointerInteractionFinished -= ApplyPendingInspectorLayout;
        Resized -= ApplyResponsiveLayout;
    }

    public void SetWorkspaceStatus(string mode, string cursor, string view, string bounds)
    {
        _modeStatus.Text = mode;
        _viewStatus.Text = $"{cursor}  {view}";
        _boundsStatus.Text = bounds;
        _modeStatus.TooltipText = mode;
        _viewStatus.TooltipText = $"{cursor}; {view}";
        _boundsStatus.TooltipText = bounds;
    }

    public void ApplyProblems(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (Node child in _problemsList.GetChildren())
        {
            _problemsList.RemoveChild(child);
            child.QueueFree();
        }

        int errors = diagnostics.Count(item =>
            item.Severity == ContentDiagnosticSeverity.ERROR);
        int warnings = diagnostics.Count - errors;
        _problemsButton.Text = $"Problems {diagnostics.Count}";
        _problemsButton.AccessibilityName =
            $"Open map problems: {Count(errors, "error")}, {Count(warnings, "warning")}";
        _problemsButton.TooltipText = diagnostics.Count == 0
            ? "No map problems"
            : $"{Count(errors, "error")}, {Count(warnings, "warning")}";
        _problemsButton.Disabled = diagnostics.Count == 0;
        if (diagnostics.Count == 0)
        {
            CloseProblems();
            return;
        }

        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            Button row = new()
            {
                Text =
                    $"{(diagnostic.Severity == ContentDiagnosticSeverity.ERROR ? "ERROR" : "WARNING")}  {diagnostic.Message}",
                AccessibilityName = $"{diagnostic.Severity}: {diagnostic.Message}",
                TooltipText = diagnostic.Source,
                Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                FocusMode = FocusModeEnum.All,
                CustomMinimumSize = new Vector2(0, 34),
            };
            row.Pressed += () => ProblemActivated?.Invoke(diagnostic);
            _problemsList.AddChild(row);
        }
    }

    public bool TryCloseProblems()
    {
        if (!_problemsOpenState)
            return false;
        CloseProblems();
        return true;
    }

    public void ShowInspector(MapEditorInspectorKind kind)
    {
        _inspectorKind = kind;
        _brushInspector.Visible = kind == MapEditorInspectorKind.BRUSH;
        _zoneInspector.Visible = kind == MapEditorInspectorKind.ZONE;
        _spawnInspector.Visible = kind == MapEditorInspectorKind.SPAWN;
        if (_canvas.PointerInteractionActive)
        {
            _inspectorLayoutPending = true;
            return;
        }

        _inspectorLayoutPending = false;
        ApplyResponsiveLayout();
    }

    public void OpenDrawer()
    {
        if (_drawerOpenState)
        {
            return;
        }

        _focusBeforeDrawer = GetViewport().GuiGetFocusOwner();
        _drawerOpenState = true;
        ApplyResponsiveLayout();
        _drawerClose.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void CloseDrawer()
    {
        if (!_drawerOpenState)
        {
            return;
        }

        _drawerOpenState = false;
        ApplyResponsiveLayout();
        Control restore = _focusBeforeDrawer is { } previous && IsInstanceValid(previous) && previous.IsVisibleInTree()
            ? previous
            : _drawerOpen;
        restore.CallDeferred(Control.MethodName.GrabFocus);
    }

    public bool TryCloseDrawer()
    {
        if (!_compact || !_drawerOpenState)
        {
            return false;
        }

        CloseDrawer();
        return true;
    }

    private void ToggleDrawer()
    {
        if (_drawerOpenState)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    public void OpenProperties()
    {
        if (_inspectorKind == MapEditorInspectorKind.EMPTY || _propertiesOpenState)
        {
            return;
        }

        _focusBeforeDrawer = GetViewport().GuiGetFocusOwner();
        _propertiesOpenState = true;
        ApplyResponsiveLayout();
        _propertiesClose.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void CloseProperties()
    {
        if (!_propertiesOpenState)
        {
            return;
        }

        _propertiesOpenState = false;
        ApplyResponsiveLayout();
        _propertiesOpen.CallDeferred(Control.MethodName.GrabFocus);
    }

    public bool TryCloseProperties()
    {
        if (!_compact || !_propertiesOpenState ||
            _inspectorKind == MapEditorInspectorKind.EMPTY)
        {
            return false;
        }

        CloseProperties();
        return true;
    }

    public void OpenStamps()
    {
        if (!_stampAvailable)
            return;
        _stampOpenState = true;
        ApplyResponsiveLayout();
    }

    public void CloseStamps()
    {
        if (!_stampOpenState)
            return;
        _stampOpenState = false;
        ApplyResponsiveLayout();
    }

    public bool TryCloseStamps()
    {
        if (!_stampOpenState)
            return false;
        CloseStamps();
        return true;
    }

    public void ToggleStamps()
    {
        if (_stampOpenState)
            CloseStamps();
        else
            OpenStamps();
    }

    public void SetStampLibraryAvailable(bool available)
    {
        if (_stampAvailable == available)
            return;
        _stampAvailable = available;
        if (!available)
            _stampOpenState = false;
        ApplyResponsiveLayout();
    }

    private void ToggleProperties()
    {
        if (_propertiesOpenState)
        {
            CloseProperties();
        }
        else
        {
            OpenProperties();
        }
    }

    public void SetInlineDockWidth(float width)
    {
        _inlineDockWidth = Math.Clamp(width, MIN_INLINE_DOCK_WIDTH, MAX_INLINE_DOCK_WIDTH);
        ApplyResponsiveLayout();
    }

    public void ShowObjectBrowserState(MapEditorEditDomain domain, bool rasterOnly)
    {
        _objectBrowser.BrushInitializationWarning.Visible =
            domain == MapEditorEditDomain.GEOMETRY && rasterOnly;
        _objectBrowser.BrushInitializationButton.Visible =
            domain == MapEditorEditDomain.GEOMETRY && rasterOnly;
    }

    public void PreserveObjectBrowserScroll(Action refresh)
    {
        int scroll = _objectBrowserScroll.ScrollVertical;
        refresh();
        Callable.From(() =>
        {
            if (IsInstanceValid(_objectBrowserScroll) && _objectBrowserScroll.IsInsideTree())
                _objectBrowserScroll.ScrollVertical = scroll;
        }).CallDeferred();
    }

    private void ToggleProblems()
    {
        if (_problemsOpenState)
            CloseProblems();
        else
            OpenProblems();
    }

    private void OpenProblems()
    {
        if (_problemsButton.Disabled)
            return;
        _focusBeforeDrawer = GetViewport().GuiGetFocusOwner();
        _problemsOpenState = true;
        ApplyResponsiveLayout();
        _problemsClose.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CloseProblems()
    {
        if (!_problemsOpenState)
            return;
        _problemsOpenState = false;
        ApplyResponsiveLayout();
        Control restore = _focusBeforeDrawer is { } previous && IsInstanceValid(previous) &&
                          previous.IsVisibleInTree()
            ? previous
            : _problemsButton;
        restore.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CaptureSceneLayout()
    {
        _inlineDockWidth = _objectDock.OffsetRight - _objectDock.OffsetLeft;
        _propertiesDockWidth = _propertiesDock.OffsetRight - _propertiesDock.OffsetLeft;
        _wideObjectDockLeft = _objectDock.OffsetLeft;
        _widePropertiesDockRight = _propertiesDock.OffsetRight;
        _dockResizeHalfWidth = (_dockResize.OffsetRight - _dockResize.OffsetLeft) * 0.5f;
        _propertiesResizeHalfWidth =
            (_propertiesResize.OffsetRight - _propertiesResize.OffsetLeft) * 0.5f;
    }

    private void ApplyResponsiveLayout()
    {
        Vector2 layoutSize = Size;
        if (layoutSize.X <= 0 || layoutSize.Y <= 0)
        {
            return;
        }

        bool wasCompact = _compact;
        _compact = layoutSize.X < COMPACT_WIDTH;
        if (_compact && !wasCompact)
        {
            _drawerOpenState = false;
            _propertiesOpenState = false;
        }
        else if (!_compact && wasCompact)
        {
            _drawerOpenState = true;
            _propertiesOpenState = true;
        }

        float problemsWidth = MathF.Min(620, MathF.Max(280, layoutSize.X - 32));
        _problemsDrawer.OffsetRight = _problemsDrawer.OffsetLeft + problemsWidth;
        _problemsDrawer.OffsetTop = _problemsDrawer.OffsetBottom -
                                    MathF.Min(360, layoutSize.Y * 0.45f);
        _problemsDrawer.Visible = _problemsOpenState;

        float objectDockWidth = _compact
            ? MathF.Max(0, MathF.Min(INLINE_DOCK_WIDTH, layoutSize.X - 32))
            : Math.Clamp(_inlineDockWidth, MIN_INLINE_DOCK_WIDTH, MAX_INLINE_DOCK_WIDTH);
        float propertiesDockWidth = _compact
            ? MathF.Max(0, MathF.Min(INLINE_DOCK_WIDTH, layoutSize.X - 32))
            : Math.Clamp(_propertiesDockWidth, MIN_INLINE_DOCK_WIDTH, MAX_INLINE_DOCK_WIDTH);
        _objectDock.SetAnchorsPreset(_compact ? LayoutPreset.RightWide : LayoutPreset.LeftWide);
        _objectDock.OffsetLeft = _compact
            ? -objectDockWidth - 16
            : _wideObjectDockLeft;
        _objectDock.OffsetRight = _compact
            ? -16
            : _wideObjectDockLeft + objectDockWidth;
        _objectDock.Visible = _drawerOpenState;
        _dockResize.OffsetLeft = _wideObjectDockLeft + objectDockWidth - _dockResizeHalfWidth;
        _dockResize.OffsetRight = _wideObjectDockLeft + objectDockWidth + _dockResizeHalfWidth;
        _dockResize.Visible = !_compact && _drawerOpenState;

        float propertiesRight = _compact ? -16 : _widePropertiesDockRight;
        float propertiesLeft = propertiesRight - propertiesDockWidth;
        _propertiesDock.OffsetLeft = propertiesLeft;
        _propertiesDock.OffsetRight = propertiesRight;
        _propertiesResize.OffsetLeft = propertiesLeft - _propertiesResizeHalfWidth;
        _propertiesResize.OffsetRight = propertiesLeft + _propertiesResizeHalfWidth;
        _drawerClose.Visible = true;
        _drawerOpen.Visible = true;
        _drawerOpen.SetPressedNoSignal(_drawerOpenState);
        _drawerOpen.TooltipText = _drawerOpenState
            ? "Hide the objects panel"
            : "Show the objects panel";
        _stampOpen.Visible = _stampAvailable;
        _stampOpen.SetPressedNoSignal(_stampOpenState);
        _stampOpen.TooltipText = _stampOpenState
            ? "Hide the stamp library"
            : "Show the stamp library";
        ApplyPropertiesVisibility();

        _canvasHost.OffsetLeft = 0;
        _canvasHost.OffsetRight = 0;
        _canvasHost.OffsetBottom = -46 - (_stampOpenState ? STAMP_DOCK_HEIGHT + 8 : 0);

        float stampLeft = _compact ? 16 : _wideObjectDockLeft + objectDockWidth + 8;
        float stampRight = _compact ? layoutSize.X - 16 : propertiesLeft - 8;
        _stampDock.SetAnchorsPreset(LayoutPreset.BottomWide);
        _stampDock.OffsetLeft = stampLeft;
        _stampDock.OffsetRight = stampRight;
        _stampDock.OffsetTop = -46 - STAMP_DOCK_HEIGHT;
        _stampDock.OffsetBottom = -46;
        _stampDock.Visible = _stampOpenState;

        _boundsStatus.Visible = layoutSize.X >= 960;
        _modeStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _viewStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _boundsStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    private void ResizeDock(InputEvent @event)
    {
        if (_compact) return;

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            _resizingDock = mouseButton.Pressed;
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion mouseMotion && _resizingDock)
        {
            SetInlineDockWidth(_inlineDockWidth + mouseMotion.Relative.X);
            AcceptEvent();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Left or Key.Right } key)
        {
            SetInlineDockWidth(_inlineDockWidth + (key.Keycode == Key.Right ? 16 : -16));
            AcceptEvent();
        }
    }

    private void ApplyPropertiesVisibility()
    {
        bool hasInspector = _inspectorKind != MapEditorInspectorKind.EMPTY;
        bool visible = _propertiesOpenState && hasInspector;
        _propertiesDock.Visible = visible;
        _inspectorFrame.Visible = visible;
        _propertiesResize.Visible = !_compact && visible;
        _propertiesOpen.Visible = true;
        _propertiesOpen.Disabled = !hasInspector;
        _propertiesOpen.SetPressedNoSignal(visible);
        _propertiesOpen.TooltipText = !hasInspector
            ? "No object selected"
            : visible
                ? "Hide the properties panel"
                : "Show the properties panel";
    }

    private void ResizeProperties(InputEvent @event)
    {
        if (_compact) return;

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
        {
            _resizingProperties = mouseButton.Pressed;
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion mouseMotion && _resizingProperties)
        {
            _propertiesDockWidth = Math.Clamp(_propertiesDockWidth - mouseMotion.Relative.X,
                MIN_INLINE_DOCK_WIDTH, MAX_INLINE_DOCK_WIDTH);
            ApplyResponsiveLayout();
            AcceptEvent();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Left or Key.Right } key)
        {
            _propertiesDockWidth = Math.Clamp(_propertiesDockWidth +
                                              (key.Keycode == Key.Left ? 16 : -16),
                MIN_INLINE_DOCK_WIDTH, MAX_INLINE_DOCK_WIDTH);
            ApplyResponsiveLayout();
            AcceptEvent();
        }
    }

    private void RequestBrushInitialization() => BrushInitializationRequested?.Invoke();

    private void ApplyPendingInspectorLayout()
    {
        if (!_inspectorLayoutPending)
        {
            return;
        }

        _inspectorLayoutPending = false;
        ApplyResponsiveLayout();
    }

    private static string Count(int count, string word) =>
        $"{count} {word}{(count == 1 ? "" : "s")}";
}
