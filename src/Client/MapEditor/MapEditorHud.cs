using System.Collections.Immutable;
using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorStatus(string Message, bool IsError = false);

public partial class MapEditorHud : Control
{
    [Export] private Label _status = null!;
    [Export] private Button _saveButton = null!;
    [Export] private MapEditorCanvas _canvas = null!;
    [Export] private Label _cursorPosition = null!;
    [Export] private Button _zoomButton = null!;
    [Export] private Button _frameButton = null!;
    [Export] private Label _mapSize = null!;
    [Export] private FileDialog _layerFileDialog = null!;
    [Export] private AcceptDialog _errorDialog = null!;
    [Export] private Control _inspectorPanel = null!;
    [Export] private Label _inspectorTitle = null!;
    [Export] private ConfirmationDialog _discardDialog = null!;
    [Export] private Control _inspectorFields = null!;
    [Export] private LineEdit _name = null!;
    [Export] private LineEdit _tags = null!;
    [Export] private SpinBox _x = null!;
    [Export] private SpinBox _y = null!;
    [Export] private SpinBox _sizeA = null!;
    [Export] private SpinBox _sizeB = null!;
    [Export] private Label _sizeALabel = null!;
    [Export] private Label _sizeBLabel = null!;
    [Export] private SpinBox _rotation = null!;
    [Export] private VBoxContainer _effectRows = null!;
    [Export] private Control _spawnInspectorFields = null!;
    [Export] private SpinBox _spawnX = null!;
    [Export] private SpinBox _spawnY = null!;
    [Export] private OptionButton _spawnTeam = null!;
    [Export] private PackedScene _effectRowScene = null!;

    private MapEditorSnapshot? _snapshot;
    private MapEditorLayer _pendingLayer;
    private bool _updatingInspector;

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
    public event Action<MapEditorLayer, string>? LayerReplaceRequested;

    public override void _Ready()
    {
        _canvas.SelectionChanged += ShowSelection;
        _canvas.SpawnSelectionChanged += ShowSpawnSelection;
        _canvas.ZonePreviewChanged += ShowZonePreview;
        _canvas.SpawnPreviewChanged += ShowSpawnPreview;
        _canvas.ZoneAddRequested += ForwardZoneAdd;
        _canvas.ZoneReplaceRequested += ForwardZoneReplace;
        _canvas.SpawnAddRequested += ForwardSpawnAdd;
        _canvas.SpawnReplaceRequested += ForwardSpawnReplace;
        _canvas.CursorMoved += ShowCursorPosition;
        _canvas.ZoomChanged += ShowZoom;
        _zoomButton.Pressed += OnZoomResetPressed;
        _frameButton.Pressed += OnFrameMapPressed;
        _layerFileDialog.FileSelected += OnLayerFileSelected;
        _name.TextChanged += OnInspectorTextChanged;
        _tags.TextChanged += OnInspectorTextChanged;
        _x.ValueChanged += OnInspectorNumberChanged;
        _y.ValueChanged += OnInspectorNumberChanged;
        _sizeA.ValueChanged += OnInspectorNumberChanged;
        _sizeB.ValueChanged += OnInspectorNumberChanged;
        _rotation.ValueChanged += OnInspectorNumberChanged;
        _spawnX.ValueChanged += OnSpawnNumberChanged;
        _spawnY.ValueChanged += OnSpawnNumberChanged;
        _spawnTeam.AddItem("Any");
        _spawnTeam.AddItem("Blue");
        _spawnTeam.AddItem("Red");
        _spawnTeam.ItemSelected += OnSpawnTeamSelected;
        _discardDialog.Confirmed += OnDiscardConfirmed;
        _discardDialog.Canceled += OnDiscardCancelled;
        SetInspectorEnabled(false);
        SetProcessUnhandledInput(Visible);
    }

    public override void _ExitTree()
    {
        _canvas.SelectionChanged -= ShowSelection;
        _canvas.SpawnSelectionChanged -= ShowSpawnSelection;
        _canvas.ZonePreviewChanged -= ShowZonePreview;
        _canvas.SpawnPreviewChanged -= ShowSpawnPreview;
        _canvas.ZoneAddRequested -= ForwardZoneAdd;
        _canvas.ZoneReplaceRequested -= ForwardZoneReplace;
        _canvas.SpawnAddRequested -= ForwardSpawnAdd;
        _canvas.SpawnReplaceRequested -= ForwardSpawnReplace;
        _canvas.CursorMoved -= ShowCursorPosition;
        _canvas.ZoomChanged -= ShowZoom;
        _zoomButton.Pressed -= OnZoomResetPressed;
        _frameButton.Pressed -= OnFrameMapPressed;
        _layerFileDialog.FileSelected -= OnLayerFileSelected;
        _name.TextChanged -= OnInspectorTextChanged;
        _tags.TextChanged -= OnInspectorTextChanged;
        _x.ValueChanged -= OnInspectorNumberChanged;
        _y.ValueChanged -= OnInspectorNumberChanged;
        _sizeA.ValueChanged -= OnInspectorNumberChanged;
        _sizeB.ValueChanged -= OnInspectorNumberChanged;
        _rotation.ValueChanged -= OnInspectorNumberChanged;
        _spawnX.ValueChanged -= OnSpawnNumberChanged;
        _spawnY.ValueChanged -= OnSpawnNumberChanged;
        _spawnTeam.ItemSelected -= OnSpawnTeamSelected;
        _discardDialog.Confirmed -= OnDiscardConfirmed;
        _discardDialog.Canceled -= OnDiscardCancelled;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.CtrlPressed || key.MetaPressed) && key.Keycode == Key.S)
        {
            RequestSave();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            BackRequested?.Invoke();
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
    public void OnDeletePressed()
    {
        if (_canvas.SelectedZoneId is { } id)
            ZoneRemoveRequested?.Invoke(id);
    }
    public void OnDeleteSpawnPressed()
    {
        if (_canvas.SelectedSpawnId is { } id)
            SpawnRemoveRequested?.Invoke(id);
    }
    public void OnAddEffectPressed()
    {
        if (_snapshot == null || _canvas.SelectedZoneId == null)
            return;
        AddEffectRow(new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 1));
        ApplyEffects();
    }
    public void OnSelectPressed() => _canvas.Tool = MapEditorTool.SELECT;
    public void OnSquarePressed() => _canvas.Tool = MapEditorTool.RECT;
    public void OnCirclePressed() => _canvas.Tool = MapEditorTool.CIRCLE;
    public void OnSpawnPressed()
    {
        _canvas.Tool = MapEditorTool.SPAWN;
        HideInspector();
    }
    public void OnZoomInPressed() => _canvas.ZoomIn();
    public void OnZoomOutPressed() => _canvas.ZoomOut();
    public void OnZoomResetPressed() => _canvas.ResetView();
    public void OnFrameMapPressed() => _canvas.FrameMap();

    public void OnReplaceBackgroundPressed() => OpenLayerFile(MapEditorLayer.BACKGROUND);
    public void OnReplaceSolidPressed() => OpenLayerFile(MapEditorLayer.SOLID);
    public void OnReplaceDestructiblePressed() => OpenLayerFile(MapEditorLayer.DESTRUCTIBLE);

    public void OnZonesToggled(bool visible)
    {
        _canvas.ShowZones = visible;
        _canvas.QueueRedraw();
    }

    public void OnDestructibleToggled(bool visible)
    {
        _canvas.ShowDestructible = visible;
        _canvas.QueueRedraw();
    }

    public void OnSolidToggled(bool visible)
    {
        _canvas.ShowSolid = visible;
        _canvas.QueueRedraw();
    }

    public void OnBackgroundToggled(bool visible)
    {
        _canvas.ShowBackground = visible;
        _canvas.QueueRedraw();
    }

    public void OnSpawnsToggled(bool visible)
    {
        _canvas.ShowSpawns = visible;
        _canvas.QueueRedraw();
    }

    public void Apply(MapEditorUpdate update)
    {
        _snapshot = update.Snapshot;
        _saveButton.Disabled = !update.Snapshot.CanSave;
        _mapSize.Text = $"{update.Snapshot.Width} x {update.Snapshot.Height} px";
        ShowDiagnostics(update.Snapshot.Diagnostics);
        _canvas.Apply(update);
        RefreshSelectedInspector();
    }

    public void ShowStatus(MapEditorStatus status)
    {
        _status.Text = status.Message;
        _status.Modulate = status.IsError ? new Color(1f, 0.45f, 0.4f) : Colors.White;
        if (!status.IsError)
            return;
        _errorDialog.DialogText = status.Message;
        _errorDialog.PopupCentered();
    }

    public void ShowDiscardConfirmation() => _discardDialog.PopupCentered();

    private void RequestSave()
    {
        if (_snapshot?.CanSave == true)
            SaveRequested?.Invoke();
    }

    private void OpenLayerFile(MapEditorLayer layer)
    {
        _pendingLayer = layer;
        _layerFileDialog.Title = $"Replace {LayerName(layer).ToLowerInvariant()} image";
        _layerFileDialog.PopupCenteredRatio(0.72f);
    }

    private void OnLayerFileSelected(string path) =>
        LayerReplaceRequested?.Invoke(_pendingLayer, path);

    private void ShowSelection(MapEditorZoneId? id)
    {
        MapEditorZone? selected = id is { } value
            ? _snapshot?.Zones.FirstOrDefault(zone => zone.Id == value)
            : null;
        if (selected == null)
        {
            if (_canvas.SelectedSpawnId == null)
                SetInspectorEnabled(false);
            return;
        }
        ShowZoneDraft(new MapEditorZoneDraft(
            selected.Name, selected.Tags, selected.Shape, selected.Effects));
    }

    private void ShowZoneDraft(MapEditorZoneDraft zone)
    {
        bool wasUpdating = _updatingInspector;
        _updatingInspector = true;
        _inspectorPanel.Show();
        SetInspectorEnabled(true);
        _spawnInspectorFields.Hide();
        _inspectorTitle.Text = zone.Shape switch
        {
            RectMapZoneShape => "Rectangle zone",
            EllipseMapZoneShape => "Oval zone",
            _ => "Circle zone",
        };
        _name.Text = zone.Name;
        _tags.Text = string.Join(", ", zone.Tags);
        _x.Value = zone.Shape.X;
        _y.Value = zone.Shape.Y;
        if (zone.Shape is RectMapZoneShape rect)
        {
            _sizeALabel.Text = "Width";
            _sizeBLabel.Text = "Height";
            _sizeA.Value = rect.Width;
            _sizeB.Value = rect.Height;
            _sizeB.Show();
            _sizeBLabel.Show();
        }
        else if (zone.Shape is EllipseMapZoneShape ellipse)
        {
            _sizeALabel.Text = "Radius X";
            _sizeBLabel.Text = "Radius Y";
            _sizeA.Value = ellipse.RadiusX;
            _sizeB.Value = ellipse.RadiusY;
            _sizeB.Show();
            _sizeBLabel.Show();
        }
        else
        {
            CircleMapZoneShape circle = (CircleMapZoneShape)zone.Shape;
            _sizeALabel.Text = "Radius";
            _sizeA.Value = circle.Radius;
            _sizeB.Hide();
            _sizeBLabel.Hide();
        }
        _rotation.Value = zone.Shape switch
        {
            RectMapZoneShape rotatedRect => rotatedRect.Rotation,
            EllipseMapZoneShape rotatedEllipse => rotatedEllipse.Rotation,
            _ => 0,
        };
        _rotation.Editable = zone.Shape is not CircleMapZoneShape;
        ClearEffectRows();
        foreach (MapZoneEffect effect in zone.Effects)
        {
            AddEffectRow(effect);
        }

        _updatingInspector = wasUpdating;
    }

    private void ShowSpawnSelection(MapEditorSpawnId? id)
    {
        int index = id is { } value ? SpawnIndex(value) : -1;
        if (_snapshot == null || index < 0)
        {
            if (_canvas.SelectedZoneId == null)
                HideInspector();
            return;
        }
        ShowSpawn(_snapshot.SpawnPoints[index].Value, index + 1);
    }

    private void ShowSpawn(MapSpawnPoint spawn, int? number = null)
    {
        bool wasUpdating = _updatingInspector;
        _updatingInspector = true;
        _inspectorPanel.Show();
        _inspectorFields.Hide();
        _spawnInspectorFields.Show();
        _inspectorTitle.Text = number is { } index ? $"Spawn {index}" : "Spawn";
        _spawnX.Value = spawn.X;
        _spawnY.Value = spawn.Y;
        _spawnTeam.Select(spawn.Team switch
        {
            Team.BLUE => 1,
            Team.RED => 2,
            _ => 0,
        });
        _updatingInspector = wasUpdating;
    }

    private void OnInspectorTextChanged(string _) => ApplyInspector();
    private void OnInspectorNumberChanged(double _) => ApplyInspector();
    private void OnSpawnNumberChanged(double _) => ApplySpawnInspector();
    private void OnSpawnTeamSelected(long _) => ApplySpawnInspector();

    private void ApplyInspector()
    {
        if (_updatingInspector || _snapshot == null || _canvas.SelectedZoneId is not { } id)
            return;
        MapEditorZone? old = _snapshot.Zones.FirstOrDefault(zone => zone.Id == id);
        if (old == null)
            return;
        int x = (int)_x.Value;
        int y = (int)_y.Value;
        MapZoneShape shape = old.Shape switch
        {
            RectMapZoneShape => new RectMapZoneShape(x, y,
                Math.Max(1, (int)_sizeA.Value), Math.Max(1, (int)_sizeB.Value),
                (float)_rotation.Value),
            EllipseMapZoneShape => new EllipseMapZoneShape(x, y,
                Math.Max(1, (int)_sizeA.Value), Math.Max(1, (int)_sizeB.Value),
                (float)_rotation.Value),
            _ => new CircleMapZoneShape(x, y, Math.Max(1, (int)_sizeA.Value)),
        };
        string name = _name.Text.StripEdges();
        string[] tags = _tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        ZoneReplaceRequested?.Invoke(id,
            new MapEditorZoneDraft(name, [.. tags], shape, old.Effects));
    }

    private void ApplySpawnInspector()
    {
        if (_updatingInspector || _canvas.SelectedSpawnId is not { } id)
            return;
        Team? team = _spawnTeam.Selected switch
        {
            1 => Team.BLUE,
            2 => Team.RED,
            _ => null,
        };
        SpawnReplaceRequested?.Invoke(id,
            new MapSpawnPoint((int)_spawnX.Value, (int)_spawnY.Value, team));
    }

    private void AddEffectRow(MapZoneEffect effect)
    {
        ZoneEffectRow row = _effectRowScene.Instantiate<ZoneEffectRow>();
        _effectRows.AddChild(row);
        row.Bind(effect);
        row.Changed += ApplyEffects;
        row.RemoveRequested += RemoveEffectRow;
    }

    private void RemoveEffectRow(ZoneEffectRow row)
    {
        _effectRows.RemoveChild(row);
        row.QueueFree();
        ApplyEffects();
    }

    private void ClearEffectRows()
    {
        foreach (Node child in _effectRows.GetChildren())
        {
            _effectRows.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ApplyEffects()
    {
        if (_updatingInspector || _snapshot == null || _canvas.SelectedZoneId is not { } id)
            return;
        MapEditorZone? old = _snapshot.Zones.FirstOrDefault(zone => zone.Id == id);
        if (old == null)
            return;
        MapZoneEffect[] effects = _effectRows.GetChildren().OfType<ZoneEffectRow>()
            .Select(row => row.Value).ToArray();
        ZoneReplaceRequested?.Invoke(id, new MapEditorZoneDraft(
            old.Name, old.Tags, old.Shape, [.. effects]));
    }

    private void SetInspectorEnabled(bool enabled)
    {
        _inspectorPanel.Visible = enabled;
        _inspectorFields.Visible = enabled;
        _spawnInspectorFields.Hide();
        _inspectorTitle.Text = enabled ? _inspectorTitle.Text : "Inspector";
    }

    private void HideInspector()
    {
        _inspectorPanel.Hide();
        _inspectorFields.Hide();
        _spawnInspectorFields.Hide();
    }

    private void ShowZonePreview(MapEditorZoneDraft? preview)
    {
        if (preview != null)
            ShowZoneDraft(preview);
        else
            RefreshSelectedInspector();
    }

    private void ShowSpawnPreview(MapSpawnPoint? preview)
    {
        if (preview is { } spawn)
            ShowSpawn(spawn);
        else
            RefreshSelectedInspector();
    }

    private void RefreshSelectedInspector()
    {
        if (_canvas.SelectedZoneId is { } zoneId)
        {
            ShowSelection(zoneId);
            return;
        }
        if (_canvas.SelectedSpawnId is { } spawnId)
        {
            ShowSpawnSelection(spawnId);
            return;
        }
        HideInspector();
    }

    private void ForwardZoneAdd(MapEditorZoneDraft draft) =>
        ZoneAddRequested?.Invoke(draft);

    private void ForwardZoneReplace(MapEditorZoneId id, MapEditorZoneDraft draft) =>
        ZoneReplaceRequested?.Invoke(id, draft);

    private void ForwardSpawnAdd(MapSpawnPoint spawn) =>
        SpawnAddRequested?.Invoke(spawn);

    private void ForwardSpawnReplace(MapEditorSpawnId id, MapSpawnPoint spawn) =>
        SpawnReplaceRequested?.Invoke(id, spawn);

    private void OnDiscardConfirmed() => DiscardConfirmed?.Invoke();

    private void OnDiscardCancelled() => DiscardCancelled?.Invoke();

    private int SpawnIndex(MapEditorSpawnId id)
    {
        if (_snapshot == null)
            return -1;
        for (int i = 0; i < _snapshot.SpawnPoints.Length; i++)
        {
            if (_snapshot.SpawnPoints[i].Id == id)
                return i;
        }
        return -1;
    }

    private void ShowCursorPosition(int x, int y) =>
        _cursorPosition.Text = $"X {x,4}   Y {y,4}";

    private void ShowZoom(float zoom) =>
        _zoomButton.Text = $"{zoom * 100:0}%";

    private void ShowDiagnostics(ImmutableArray<ContentDiagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty)
        {
            _status.Text = _snapshot?.Dirty == true ? "Unsaved changes" : string.Empty;
            _status.Modulate = Colors.White;
            return;
        }

        bool hasErrors = diagnostics.Any(
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
        _status.Text = string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Message));
        _status.Modulate = hasErrors
            ? new Color(1f, 0.45f, 0.4f)
            : new Color(1f, 0.8f, 0.35f);
    }

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };
}
