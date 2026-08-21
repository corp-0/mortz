using System.Collections.Immutable;
using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.MapEditor;

public partial class MapEditorObjectBrowser : VBoxContainer
{
    [Export] private Label _initializationWarning = null!;
    [Export] private Label _initializationDetail = null!;
    [Export] private Button _initializationButton = null!;
    [Export] private MapEditorObjectBrowserLayer _backgroundLayer = null!;
    [Export] private HSeparator _backgroundSeparator = null!;
    [Export] private MapEditorObjectBrowserLayer _solidLayer = null!;
    [Export] private HSeparator _solidSeparator = null!;
    [Export] private MapEditorObjectBrowserLayer _destructibleLayer = null!;
    [Export] private CheckButton _zonesVisibility = null!;
    [Export] private Label _zonesEmpty = null!;
    [Export] private CheckButton _spawnsVisibility = null!;
    [Export] private Label _spawnsEmpty = null!;
    [Export] private PackedScene _brushRowScene = null!;
    [Export] private PackedScene _itemRowScene = null!;

    private readonly Dictionary<MapEditorLayer, bool> _expandedLayers = new()
    {
        [MapEditorLayer.BACKGROUND] = true,
        [MapEditorLayer.SOLID] = true,
        [MapEditorLayer.DESTRUCTIBLE] = true,
    };

    private MapEditorSnapshot? _snapshot;
    private MapEditorEditDomain _domain;
    private MapEditorLayer _selectedLayer;
    private MapEditorBrushId? _selectedBrush;
    private MapEditorZoneId? _selectedZone;
    private MapEditorSpawnId? _selectedSpawn;
    private bool _showBackground = true;
    private bool _showSolid = true;
    private bool _showDestructible = true;
    private bool _showZones = true;
    private bool _showSpawns = true;
    private readonly List<Control> _domainRows = [];
    private readonly ButtonGroup _layerGroup = new() { AllowUnpress = false };

    public override void _Ready()
    {
        _initializationWarning.AddThemeColorOverride("font_color", MapEditorInspectorUi.Warning);
        _initializationButton.Pressed += () => BrushInitializationRequested?.Invoke();
        BindLayer(_backgroundLayer, MapEditorLayer.BACKGROUND);
        BindLayer(_solidLayer, MapEditorLayer.SOLID);
        BindLayer(_destructibleLayer, MapEditorLayer.DESTRUCTIBLE);
        _zonesVisibility.Toggled += visible => ZonesVisibilityChanged?.Invoke(visible);
        _spawnsVisibility.Toggled += visible => SpawnsVisibilityChanged?.Invoke(visible);
        HideAll();
    }

    public Label BrushInitializationWarning => _initializationWarning;
    public Button BrushInitializationButton => _initializationButton;

    public event Action? BrushInitializationRequested;
    public event Action<MapEditorLayer>? LayerSelected;
    public event Action<MapEditorLayer, bool>? LayerVisibilityChanged;
    public event Action<MapEditorBrushId, bool>? BrushVisibilityChanged;
    public event Action<MapEditorLayer, MapEditorBrushId>? BrushSelected;
    public event Action<MapEditorBrushId, int>? BrushReorderRequested;
    public event Action<MapEditorZoneId>? ZoneSelected;
    public event Action<MapEditorSpawnId>? SpawnSelected;
    public event Action<bool>? ZonesVisibilityChanged;
    public event Action<bool>? SpawnsVisibilityChanged;
    public event Action<MapEditorLayer, MapEditorBrushId>? BrushFrameRequested;
    public event Action<MapEditorZoneId>? ZoneFrameRequested;
    public event Action<MapEditorSpawnId>? SpawnFrameRequested;

    public void Apply(MapEditorSnapshot snapshot, MapEditorEditDomain domain,
        MapEditorLayer selectedLayer, MapEditorBrushId? selectedBrush,
        MapEditorZoneId? selectedZone, MapEditorSpawnId? selectedSpawn,
        bool showBackground, bool showSolid, bool showDestructible,
        bool showZones, bool showSpawns)
    {
        _snapshot = snapshot;
        _domain = domain;
        _selectedLayer = selectedLayer;
        _selectedBrush = selectedBrush;
        _selectedZone = selectedZone;
        _selectedSpawn = selectedSpawn;
        _showBackground = showBackground;
        _showSolid = showSolid;
        _showDestructible = showDestructible;
        _showZones = showZones;
        _showSpawns = showSpawns;
        Rebuild();
    }

    private void Rebuild()
    {
        ClearRows();
        HideAll();
        if (_snapshot == null)
        {
            return;
        }

        switch (_domain)
        {
            case MapEditorEditDomain.GEOMETRY:
                BuildGeometry();
                break;
            case MapEditorEditDomain.ZONES:
                BuildZones();
                break;
            case MapEditorEditDomain.SPAWNS:
                BuildSpawns();
                break;
        }
    }

    private void BuildGeometry()
    {
        if (_snapshot!.SourceStatus == MapEditorRasterSourceStatus.OBSOLETE ||
            _snapshot.BrushDocument == null)
        {
            _initializationWarning.Visible = true;
            _initializationDetail.Visible = true;
            _initializationButton.Visible = true;
            return;
        }

        _backgroundSeparator.Visible = true;
        _solidSeparator.Visible = true;
        ApplyLayer(_backgroundLayer, MapEditorLayer.BACKGROUND);
        ApplyLayer(_solidLayer, MapEditorLayer.SOLID);
        ApplyLayer(_destructibleLayer, MapEditorLayer.DESTRUCTIBLE);
    }

    private void ApplyLayer(MapEditorObjectBrowserLayer section, MapEditorLayer layer)
    {
        MapEditorLayerSource source = _snapshot!.BrushDocument!.Layers.Get(layer);
        int missing = source.Brushes.Count(brush => TextureStatus(brush) !=
                                                    MapEditorTextureResolutionStatus.RESOLVED);
        int errors = LayerErrorCount(layer, source.Brushes);
        string state = source.BakeDirty ? "Unsaved" : "Saved";
        section.Visible = true;
        section.ExpandButton.Text = _expandedLayers[layer] ? "▾" : "▸";
        section.ExpandButton.AccessibilityName =
            $"{(_expandedLayers[layer] ? "Collapse" : "Expand")} {LayerName(layer)} layer";
        section.SelectButton.Text = LayerName(layer);
        section.SelectButton.AccessibilityName = $"Select {LayerName(layer)} layer";
        section.SelectButton.TooltipText = $"Draw new shapes on the {LayerName(layer)} layer.";
        section.SelectButton.SetPressedNoSignal(layer == _selectedLayer);
        section.VisibilityButton.SetPressedNoSignal(LayerVisible(layer));
        section.VisibilityButton.AccessibilityName = $"{LayerName(layer)} layer visibility";
        section.SummaryLabel.Text =
            $"{Count(source.Brushes.Length, "brush")} * {state} * " +
            $"{missing} missing * {Count(errors, "problem")}";
        section.SummaryLabel.TooltipText = section.SummaryLabel.Text;
        MapEditorInspectorUi.Metadata(section.SummaryLabel, errors > 0
            ? MapEditorInspectorUi.Danger
            : missing > 0 || source.BakeDirty
                ? MapEditorInspectorUi.Warning
                : MapEditorInspectorUi.MutedText);
        section.Brushes.Visible = _expandedLayers[layer];
        if (!_expandedLayers[layer])
        {
            return;
        }

        section.EmptyLabel.Visible = source.Brushes.IsEmpty;
        for (int index = source.Brushes.Length - 1; index >= 0; index--)
        {
            section.Brushes.AddChild(BuildBrushRow(layer, source.Brushes[index], index,
                source.Brushes.Length));
        }
    }

    private MapEditorObjectBrowserBrushRow BuildBrushRow(MapEditorLayer layer,
        MapEditorBrush brush, int index, int count)
    {
        MapEditorObjectBrowserBrushRow row =
            _brushRowScene.Instantiate<MapEditorObjectBrowserBrushRow>();
        row.Name = $"Brush{brush.Id.Value}";
        row.SelectButton.Text = brush.Name;
        row.SelectButton.AccessibilityName = $"Select brush {brush.Name}";
        row.SelectButton.TooltipText =
            $"{ShapeName(brush.Shape)} * {index + 1} of {count}";
        row.SelectButton.SetPressedNoSignal(brush.Id == _selectedBrush);
        row.SelectButton.Pressed += () => BrushSelected?.Invoke(layer, brush.Id);
        row.SelectButton.GuiInput += input => FrameOnDoubleClick(input, () =>
        {
            BrushSelected?.Invoke(layer, brush.Id);
            BrushFrameRequested?.Invoke(layer, brush.Id);
        });
        row.VisibilityButton.SetPressedNoSignal(brush.Visible);
        row.VisibilityButton.AccessibilityName = $"Toggle visibility of {brush.Name}";
        row.VisibilityButton.TooltipText =
            $"{(brush.Visible ? "Hide" : "Show")} {brush.Name} in the editor and saved map";
        row.VisibilityButton.Toggled += visible =>
        {
            row.VisibilityButton.TooltipText =
                $"{(visible ? "Hide" : "Show")} {brush.Name} in the editor and saved map";
            BrushVisibilityChanged?.Invoke(brush.Id, visible);
        };
        row.OrderControls.Visible = brush.Id == _selectedBrush;
        row.LowerButton.AccessibilityName = $"Move {brush.Name} down in the object list";
        row.LowerButton.TooltipText = $"Move {brush.Name} down. It will be drawn earlier.";
        row.LowerButton.Disabled = index == 0;
        row.LowerButton.Pressed += () => BrushReorderRequested?.Invoke(brush.Id, index - 1);
        row.RaiseButton.AccessibilityName = $"Move {brush.Name} up in the object list";
        row.RaiseButton.TooltipText = $"Move {brush.Name} up. It will be drawn later.";
        row.RaiseButton.Disabled = index == count - 1;
        row.RaiseButton.Pressed += () => BrushReorderRequested?.Invoke(brush.Id, index + 1);

        MapEditorTextureResolutionStatus textureStatus = TextureStatus(brush);
        string material = brush.Material switch
        {
            MapEditorSolidColorMaterial solid => $"Color {solid.Color.Html}",
            _ when textureStatus == MapEditorTextureResolutionStatus.RESOLVED => "Texture ready",
            _ when textureStatus == MapEditorTextureResolutionStatus.MISSING => "Texture missing",
            _ => "Texture error",
        };
        row.DetailsLabel.Text =
            $"{ShapeName(brush.Shape)} * {material} * {index + 1}/{count}";
        row.DetailsLabel.TooltipText = brush.Material switch
        {
            MapEditorTextureMaterial texture => texture.Reference.Location,
            MapEditorSolidColorMaterial solid => solid.Color.Html,
            _ => string.Empty,
        };
        MapEditorInspectorUi.Metadata(row.DetailsLabel,
            textureStatus == MapEditorTextureResolutionStatus.RESOLVED
                ? MapEditorInspectorUi.MutedText
                : MapEditorInspectorUi.Warning);
        return row;
    }

    private void BuildZones()
    {
        ConfigureOverlay(_zonesVisibility, "Zones", _showZones);
        if (_snapshot!.Zones.IsEmpty)
        {
            _zonesEmpty.Visible = true;
            return;
        }

        for (int index = 0; index < _snapshot.Zones.Length; index++)
        {
            MapEditorZone zone = _snapshot.Zones[index];
            int issues = ZoneErrorCount(index);
            Button row = _itemRowScene.Instantiate<Button>();
            row.Name = $"Zone{zone.Id.Value}";
            row.Text = $"{zone.Name} * {ZoneShapeName(zone.Shape)}" +
                       (issues == 0 ? "" : $" * {Count(issues, "problem")}");
            row.AccessibilityName = $"Select zone {zone.Name}";
            row.TooltipText = issues == 0
                ? $"{ZoneShapeName(zone.Shape)} zone * No problems"
                : $"{ZoneShapeName(zone.Shape)} zone * {Count(issues, "problem")}";
            row.ToggleMode = true;
            row.SetPressedNoSignal(zone.Id == _selectedZone);
            row.Pressed += () => ZoneSelected?.Invoke(zone.Id);
            row.GuiInput += input => FrameOnDoubleClick(input, () =>
            {
                ZoneSelected?.Invoke(zone.Id);
                ZoneFrameRequested?.Invoke(zone.Id);
            });
            AddChild(row);
            _domainRows.Add(row);
        }
    }

    private void BuildSpawns()
    {
        ConfigureOverlay(_spawnsVisibility, "Spawns", _showSpawns);
        if (_snapshot!.SpawnPoints.IsEmpty)
        {
            _spawnsEmpty.Visible = true;
            return;
        }

        for (int index = 0; index < _snapshot.SpawnPoints.Length; index++)
        {
            MapEditorSpawn spawn = _snapshot.SpawnPoints[index];
            string team = spawn.Value.Team switch
            {
                Team.BLUE => "Blue team",
                Team.RED => "Red team",
                _ => "Any team",
            };
            Button row = _itemRowScene.Instantiate<Button>();
            row.Name = $"Spawn{spawn.Id.Value}";
            row.Text = $"Spawn {index + 1} * {team} * ({spawn.Value.X}, {spawn.Value.Y})";
            row.AccessibilityName = $"Select spawn {index + 1}";
            row.TooltipText = $"{team} * X {spawn.Value.X}, Y {spawn.Value.Y}";
            row.ToggleMode = true;
            row.SetPressedNoSignal(spawn.Id == _selectedSpawn);
            row.Pressed += () => SpawnSelected?.Invoke(spawn.Id);
            row.GuiInput += input => FrameOnDoubleClick(input, () =>
            {
                SpawnSelected?.Invoke(spawn.Id);
                SpawnFrameRequested?.Invoke(spawn.Id);
            });
            AddChild(row);
            _domainRows.Add(row);
        }
    }

    private void BindLayer(MapEditorObjectBrowserLayer section, MapEditorLayer layer)
    {
        section.SelectButton.ButtonGroup = _layerGroup;
        section.ExpandButton.Pressed += () =>
        {
            _expandedLayers[layer] = !_expandedLayers[layer];
            Rebuild();
        };
        section.SelectButton.Pressed += () => LayerSelected?.Invoke(layer);
        section.VisibilityButton.Toggled += visible =>
        {
            LayerVisibilityChanged?.Invoke(layer, visible);
        };
    }

    private static void ConfigureOverlay(CheckButton toggle, string text, bool visible)
    {
        toggle.Visible = true;
        toggle.Text = $"{text}: {(visible ? "Shown" : "Hidden")}";
        toggle.SetPressedNoSignal(visible);
    }

    private void ClearRows()
    {
        ClearLayerRows(_backgroundLayer);
        ClearLayerRows(_solidLayer);
        ClearLayerRows(_destructibleLayer);
        foreach (Control row in _domainRows)
        {
            RemoveChild(row);
            row.QueueFree();
        }

        _domainRows.Clear();
    }

    private static void ClearLayerRows(MapEditorObjectBrowserLayer layer)
    {
        foreach (Node child in layer.Brushes.GetChildren())
        {
            if (child == layer.EmptyLabel)
            {
                continue;
            }

            layer.Brushes.RemoveChild(child);
            child.QueueFree();
        }

        layer.EmptyLabel.Visible = false;
    }

    private void HideAll()
    {
        _initializationWarning.Visible = false;
        _initializationDetail.Visible = false;
        _initializationButton.Visible = false;
        _backgroundLayer.Visible = false;
        _backgroundSeparator.Visible = false;
        _solidLayer.Visible = false;
        _solidSeparator.Visible = false;
        _destructibleLayer.Visible = false;
        _zonesVisibility.Visible = false;
        _zonesEmpty.Visible = false;
        _spawnsVisibility.Visible = false;
        _spawnsEmpty.Visible = false;
    }

    private int LayerErrorCount(MapEditorLayer layer, ImmutableArray<MapEditorBrush> brushes)
    {
        string layerName = layer.ToString();
        return _snapshot!.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == ContentDiagnosticSeverity.ERROR &&
            (StartsWithWord(diagnostic.Message, layerName) ||
             brushes.Any(brush => IsBrushDiagnostic(diagnostic, brush.Id))));
    }

    private int ZoneErrorCount(int index) => _snapshot!.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == ContentDiagnosticSeverity.ERROR &&
        diagnostic.Message.Contains($"zones[{index}].", StringComparison.OrdinalIgnoreCase));

    private MapEditorTextureResolutionStatus TextureStatus(MapEditorBrush brush)
    {
        ContentDiagnostic? diagnostic = _snapshot!.Diagnostics.FirstOrDefault(candidate =>
            IsBrushDiagnostic(candidate, brush.Id) &&
            candidate.Message.Contains(" texture is ", StringComparison.OrdinalIgnoreCase));
        if (diagnostic == null)
            return MapEditorTextureResolutionStatus.RESOLVED;
        if (diagnostic.Message.Contains(" texture is missing:", StringComparison.OrdinalIgnoreCase))
            return MapEditorTextureResolutionStatus.MISSING;
        return MapEditorTextureResolutionStatus.LOAD_ERROR;
    }

    private static bool IsBrushDiagnostic(ContentDiagnostic diagnostic, MapEditorBrushId id)
    {
        string prefix = $"Brush {id.Value}";
        return diagnostic.Message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               diagnostic.Message.Length > prefix.Length &&
               char.IsWhiteSpace(diagnostic.Message[prefix.Length]);
    }

    private static bool StartsWithWord(string value, string word) =>
        value.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
        (value.Length == word.Length || !char.IsLetterOrDigit(value[word.Length]));

    private bool LayerVisible(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => _showBackground,
        MapEditorLayer.SOLID => _showSolid,
        MapEditorLayer.DESTRUCTIBLE => _showDestructible,
        _ => false,
    };

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => layer.ToString(),
    };

    private static string ShapeName(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape => "Rectangle",
        MapEditorEllipseBrushShape => "Ellipse",
        MapEditorPolygonBrushShape => "Polygon",
        _ => "Unknown shape",
    };

    private static string ZoneShapeName(MapZoneShape shape) => shape switch
    {
        RectMapZoneShape => "Rectangle",
        EllipseMapZoneShape => "Ellipse",
        CircleMapZoneShape => "Circle",
        _ => "Unknown shape",
    };

    private static string Count(int count, string word) =>
        $"{count} {word}{(count == 1 ? "" : "s")}";

    private static void FrameOnDoubleClick(InputEvent input, Action frame)
    {
        if (input is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
                DoubleClick: true,
            })
            frame();
    }
}
