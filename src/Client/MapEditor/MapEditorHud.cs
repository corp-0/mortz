using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Client.MapEditor;

[Meta(typeof(IAutoNode))]
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

    private MapEditorDocument? _document;
    private MapEditorLayer _pendingLayer;
    private bool _updatingInspector;

    [Dependency]
    private MapEditor Editor => this.DependOn<MapEditor>();

    public override void _Notification(int what) => this.Notify(what);

    public override void _Ready()
    {
        _canvas.SelectionChanged += ShowSelection;
        _canvas.SpawnSelectionChanged += ShowSpawnSelection;
        _canvas.DocumentChanged += MarkDirty;
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
        SetInspectorEnabled(false);
        SetProcessUnhandledInput(Visible);
    }

    public void OnResolved()
    {
        Editor.MapLoaded += ShowMap;
        Editor.LayerChanged += ShowLayer;
        Editor.StatusChanged += SetStatus;
        Editor.DirtyChanged += ShowDirty;
        Editor.DiscardRequested += ShowDiscardDialog;
        _discardDialog.Confirmed += Editor.ConfirmDiscard;
    }

    public override void _ExitTree()
    {
        _canvas.SelectionChanged -= ShowSelection;
        _canvas.SpawnSelectionChanged -= ShowSpawnSelection;
        _canvas.DocumentChanged -= MarkDirty;
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
        _discardDialog.Confirmed -= Editor.ConfirmDiscard;
        Editor.MapLoaded -= ShowMap;
        Editor.LayerChanged -= ShowLayer;
        Editor.StatusChanged -= SetStatus;
        Editor.DirtyChanged -= ShowDirty;
        Editor.DiscardRequested -= ShowDiscardDialog;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.CtrlPressed || key.MetaPressed) && key.Keycode == Key.S)
        {
            Editor.Save();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            Editor.Close();
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

    public void OnReloadPressed() => Editor.Reload();
    public void OnSavePressed() => Editor.Save();
    public void OnBackPressed() => Editor.Close();
    public void OnDeletePressed() => _canvas.DeleteSelected();
    public void OnDeleteSpawnPressed() => _canvas.DeleteSelectedSpawn();
    public void OnAddEffectPressed()
    {
        if (_document == null || _canvas.SelectedIndex < 0)
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

    private void ShowMap(MapEditorMapLoaded map)
    {
        _document = map.Document;
        _canvas.SetMap(map.Background, map.Solid, map.Destructible, map.Document);
        _mapSize.Text = $"{map.Background.GetWidth()} x {map.Background.GetHeight()} px";
    }

    private void ShowLayer(MapEditorLayer layer, Image image) => _canvas.SetLayer(layer, image);

    private void OpenLayerFile(MapEditorLayer layer)
    {
        _pendingLayer = layer;
        _layerFileDialog.Title = $"Replace {LayerName(layer).ToLowerInvariant()} image";
        _layerFileDialog.PopupCenteredRatio(0.72f);
    }

    private void OnLayerFileSelected(string path) => Editor.ReplaceLayer(_pendingLayer, path);

    private void ShowSelection(int index)
    {
        if (_document == null || index < 0 || index >= _document.Zones.Count)
        {
            SetInspectorEnabled(false);
            return;
        }
        MapZoneDef zone = _document.Zones[index];
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

        _updatingInspector = false;
    }

    private void ShowSpawnSelection(int index)
    {
        if (_document == null || index < 0 || index >= _document.SpawnPoints.Count)
        {
            HideInspector();
            return;
        }
        MapSpawnPoint spawn = _document.SpawnPoints[index];
        _updatingInspector = true;
        _inspectorPanel.Show();
        _inspectorFields.Hide();
        _spawnInspectorFields.Show();
        _inspectorTitle.Text = $"Spawn {index + 1}";
        _spawnX.Value = spawn.X;
        _spawnY.Value = spawn.Y;
        _spawnTeam.Select(spawn.Team switch
        {
            Team.BLUE => 1,
            Team.RED => 2,
            _ => 0,
        });
        _updatingInspector = false;
    }

    private void OnInspectorTextChanged(string _) => ApplyInspector();
    private void OnInspectorNumberChanged(double _) => ApplyInspector();
    private void OnSpawnNumberChanged(double _) => ApplySpawnInspector();
    private void OnSpawnTeamSelected(long _) => ApplySpawnInspector();

    private void ApplyInspector()
    {
        if (_updatingInspector || _document == null || _canvas.SelectedIndex < 0)
            return;
        MapZoneDef old = _document.Zones[_canvas.SelectedIndex];
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
        if (name.Length == 0)
            name = old.Name;
        string[] tags = _tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        _canvas.ReplaceSelected(old with { Name = name, Tags = tags, Shape = shape });
    }

    private void ApplySpawnInspector()
    {
        if (_updatingInspector || _canvas.SelectedSpawnIndex < 0)
            return;
        Team? team = _spawnTeam.Selected switch
        {
            1 => Team.BLUE,
            2 => Team.RED,
            _ => null,
        };
        _canvas.ReplaceSelectedSpawn(new MapSpawnPoint(
            (int)_spawnX.Value, (int)_spawnY.Value, team));
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
        if (_updatingInspector || _document == null || _canvas.SelectedIndex < 0)
            return;
        MapZoneDef old = _document.Zones[_canvas.SelectedIndex];
        MapZoneEffect[] effects = _effectRows.GetChildren().OfType<ZoneEffectRow>()
            .Select(row => row.Value).ToArray();
        _canvas.ReplaceSelected(old with { Effects = effects });
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

    private void MarkDirty() => Editor.MarkChanged();

    private void ShowCursorPosition(int x, int y) =>
        _cursorPosition.Text = $"X {x,4}   Y {y,4}";

    private void ShowZoom(float zoom) =>
        _zoomButton.Text = $"{zoom * 100:0}%";

    private void ShowDirty(bool dirty) => _saveButton.Disabled = !dirty;

    private void ShowDiscardDialog() => _discardDialog.PopupCentered();

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.Modulate = error ? new Color(1f, 0.45f, 0.4f) : Colors.White;
        if (error)
        {
            _errorDialog.DialogText = text;
            _errorDialog.PopupCentered();
        }
    }

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };
}
