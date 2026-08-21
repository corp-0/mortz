using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorStampCard : Button
{
    [Export] private MapEditorStampPreview _preview = null!;
    [Export] private Label _nameLabel = null!;
    [Export] private Label _detailLabel = null!;
    [Export] private Button _deleteButton = null!;

    public event Action? Selected;
    public event Action? DeleteRequested;

    public override void _Ready()
    {
        Pressed += OnSelected;
        _deleteButton.Pressed += OnDeleteRequested;
    }

    public override void _ExitTree()
    {
        Pressed -= OnSelected;
        _deleteButton.Pressed -= OnDeleteRequested;
    }

    public void Apply(MapEditorStamp stamp, bool selected, MapEditorCanvasResources resources)
    {
        Disabled = false;
        _deleteButton.Show();
        _deleteButton.Disabled = false;
        ToggleMode = true;
        Text = string.Empty;
        _preview.Show();
        _preview.Apply(stamp.Brush, resources);
        _nameLabel.Text = stamp.Name;
        _detailLabel.Text = $"{LayerName(stamp.Brush.Layer)} · {ShapeName(stamp.Brush.Shape)}";
        TooltipText = $"Paint {stamp.Name}";
        AccessibilityName = $"Select {stamp.Name} stamp";
        SetPressedNoSignal(selected);
    }

    public void ApplySaveAction(bool enabled, MapEditorCanvasResources resources)
    {
        Disabled = !enabled;
        _deleteButton.Hide();
        ToggleMode = false;
        Text = string.Empty;
        _preview.Show();
        _preview.Apply(null, resources);
        _nameLabel.Text = "+  SAVE SELECTED";
        _detailLabel.Text = "AS STAMP";
        TooltipText = enabled
            ? "Save the selected geometry object as a reusable stamp"
            : "Select a geometry object to save it as a stamp";
        AccessibilityName = "Save selected geometry as a stamp";
    }

    public void ApplyEmptyState(MapEditorCanvasResources resources)
    {
        Disabled = true;
        _deleteButton.Hide();
        ToggleMode = false;
        Text = string.Empty;
        _preview.Show();
        _preview.Apply(null, resources);
        _nameLabel.Text = "NO STAMPS YET";
        _detailLabel.Text = "Save selected geometry to start";
    }

    private void OnSelected() => Selected?.Invoke();

    private void OnDeleteRequested() => DeleteRequested?.Invoke();

    private static string ShapeName(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape => "Rectangle",
        MapEditorEllipseBrushShape => "Ellipse",
        MapEditorPolygonBrushShape => "Polygon",
        _ => "Geometry",
    };

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => layer.ToString(),
    };
}
