using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorToolbar : HBoxContainer
{
    [Export] private Button _geometryDomain = null!;
    [Export] private Button _zonesDomain = null!;
    [Export] private Button _spawnsDomain = null!;
    [Export] private Button _selectTool = null!;
    [Export] private Button _zoneRectangleTool = null!;
    [Export] private Button _zoneEllipseTool = null!;
    [Export] private Button _spawnTool = null!;
    [Export] private Button _brushRectangleTool = null!;
    [Export] private Button _brushEllipseTool = null!;
    [Export] private Button _brushPolygonTool = null!;
    private readonly Dictionary<MapEditorEditDomain, Button> _domainButtons = [];
    private readonly Dictionary<MapEditorTool, Button> _toolButtons = [];

    public event Action<MapEditorEditDomain>? DomainSelected;
    public event Action<MapEditorTool>? ToolSelected;

    public override void _Ready()
    {
        ConfigureDomains();
        ConfigureTools();
        ConfigureFocusOrder();
    }

    public void ApplyDomain(MapEditorEditDomain domain)
    {
        foreach ((MapEditorEditDomain value, Button button) in _domainButtons)
        {
            button.SetPressedNoSignal(value == domain);
        }

        _selectTool.Visible = true;
        _zoneRectangleTool.Visible = domain == MapEditorEditDomain.ZONES;
        _zoneEllipseTool.Visible = domain == MapEditorEditDomain.ZONES;
        _spawnTool.Visible = domain == MapEditorEditDomain.SPAWNS;
        _brushRectangleTool.Visible = domain == MapEditorEditDomain.GEOMETRY;
        _brushEllipseTool.Visible = domain == MapEditorEditDomain.GEOMETRY;
        _brushPolygonTool.Visible = domain == MapEditorEditDomain.GEOMETRY;
    }

    public void ApplyTool(MapEditorTool tool)
    {
        foreach ((MapEditorTool value, Button button) in _toolButtons)
        {
            button.SetPressedNoSignal(value == tool);
        }
    }

    public void SetCompact(bool compact)
    {
        SetDomainLabel(_geometryDomain, compact, "Geometry", "Geo");
        SetDomainLabel(_zonesDomain, compact, "Zones", "Zon");
        SetDomainLabel(_spawnsDomain, compact, "Spawns", "Spa");
        SetToolLabel(_selectTool, compact, "Select", "S");
        SetToolLabel(_zoneRectangleTool, compact, "Rectangle", "□");
        SetToolLabel(_zoneEllipseTool, compact, "Ellipse", "○");
        SetToolLabel(_spawnTool, compact, "Spawn", "+");
        SetToolLabel(_brushRectangleTool, compact, "Rectangle", "□");
        SetToolLabel(_brushEllipseTool, compact, "Ellipse", "○");
        SetToolLabel(_brushPolygonTool, compact, "Polygon", "△");
    }

    private void ConfigureDomains()
    {
        ButtonGroup group = new() { AllowUnpress = false };
        AddDomain(MapEditorEditDomain.GEOMETRY, _geometryDomain,
            "Draw and edit terrain.", group);
        AddDomain(MapEditorEditDomain.ZONES, _zonesDomain,
            "Create and edit gameplay zones.", group);
        AddDomain(MapEditorEditDomain.SPAWNS, _spawnsDomain,
            "Create and edit spawn points.", group);
    }

    private void AddDomain(MapEditorEditDomain domain, Button button, string tooltip,
        ButtonGroup group)
    {
        button.ButtonGroup = group;
        button.TooltipText = tooltip;
        button.Pressed += () => DomainSelected?.Invoke(domain);
        _domainButtons.Add(domain, button);
    }

    private void ConfigureTools()
    {
        ButtonGroup group = new() { AllowUnpress = false };
        AddTool(MapEditorTool.SELECT, _selectTool, "Select and move objects.", group);
        AddTool(MapEditorTool.RECT, _zoneRectangleTool,
            "Draw a rectangular zone.", group);
        AddTool(MapEditorTool.CIRCLE, _zoneEllipseTool,
            "Draw an elliptical zone.", group);
        AddTool(MapEditorTool.SPAWN, _spawnTool,
            "Place a spawn point.", group);
        AddTool(MapEditorTool.BRUSH_RECT, _brushRectangleTool,
            "Draw a rectangle on the selected layer.", group);
        AddTool(MapEditorTool.BRUSH_ELLIPSE, _brushEllipseTool,
            "Draw an ellipse on the selected layer.", group);
        AddTool(MapEditorTool.BRUSH_POLYGON, _brushPolygonTool,
            "Draw a polygon on the selected layer.", group);
    }

    private void AddTool(MapEditorTool tool, Button button, string tooltip, ButtonGroup group)
    {
        button.ButtonGroup = group;
        button.TooltipText = tooltip;
        button.Pressed += () => ToolSelected?.Invoke(tool);
        _toolButtons.Add(tool, button);
    }

    private void ConfigureFocusOrder()
    {
        Control[] controls =
        [
            _geometryDomain, _zonesDomain, _spawnsDomain, _selectTool, _zoneRectangleTool,
            _zoneEllipseTool, _spawnTool, _brushRectangleTool, _brushEllipseTool,
            _brushPolygonTool,
        ];
        for (int index = 0; index < controls.Length; index++)
        {
            controls[index].FocusNext = controls[index].GetPathTo(controls[(index + 1) % controls.Length]);
            controls[index].FocusPrevious =
                controls[index].GetPathTo(controls[(index - 1 + controls.Length) % controls.Length]);
        }
    }

    private static void SetDomainLabel(Button button, bool compact, string wide, string compactText) =>
        button.Text = compact ? compactText : wide;

    private static void SetToolLabel(Button button, bool compact, string wide, string compactText) =>
        button.Text = compact ? compactText : wide;
}
