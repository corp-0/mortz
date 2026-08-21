using System.Collections.Immutable;
using Godot;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorBrushInspectorValue(
    MapEditorBrushId Id,
    MapEditorBrushDraft Brush,
    bool MaterialMissing,
    string? Diagnostic = null);

public partial class MapEditorBrushInspector : ScrollContainer
{
    [Export] private Label _title = null!;
    [Export] private MapEditorInspectorField _name = null!;
    [Export] private OptionButton _layer = null!;
    [Export] private CheckButton _visible = null!;
    [Export] private Label _shape = null!;
    [Export] private MapEditorInspectorField _x = null!;
    [Export] private MapEditorInspectorField _y = null!;
    [Export] private MapEditorInspectorField _width = null!;
    [Export] private MapEditorInspectorField _height = null!;
    [Export] private MapEditorInspectorField _rotation = null!;
    [Export] private MapEditorInspectorField _vertices = null!;
    [Export] private MapEditorMaterialPicker _material = null!;
    [Export] private Label _materialStatus = null!;
    [Export] private OptionButton _projectionMode = null!;
    [Export] private MapEditorInspectorField _originX = null!;
    [Export] private MapEditorInspectorField _originY = null!;
    [Export] private MapEditorInspectorField _scaleX = null!;
    [Export] private MapEditorInspectorField _scaleY = null!;
    [Export] private MapEditorInspectorField _projectionRotation = null!;
    [Export] private Label _diagnostic = null!;
    [Export] private Button _duplicate = null!;
    [Export] private Button _delete = null!;

    private MapEditorInspectorField[] _fields = null!;
    private MapEditorBrushInspectorValue? _value;
    private bool _applying;

    public override void _Ready()
    {
        _name.Configure("Name", "Name", "Brush name");
        _x.Configure("X", "X", "Brush X position");
        _y.Configure("Y", "Y", "Brush Y position");
        _rotation.Configure("Rotation", "Rotation", "Brush rotation");
        _vertices.Configure("Vertices", "Vertices", "Brush polygon vertices");
        _originX.Configure("OriginX", "Offset X", "Texture offset X");
        _originY.Configure("OriginY", "Offset Y", "Texture offset Y");
        _scaleX.Configure("ScaleX", "Scale X", "Horizontal texture scale");
        _scaleY.Configure("ScaleY", "Scale Y", "Vertical texture scale");
        _projectionRotation.Configure("ProjectionRotation", "Rotation", "Texture rotation");
        _fields =
        [
            _name, _x, _y, _width, _height, _rotation, _vertices,
            _originX, _originY, _scaleX, _scaleY, _projectionRotation
        ];
        foreach (MapEditorInspectorField field in _fields)
        {
            field.PreviewRequested += Preview;
            field.CommitRequested += Commit;
            field.CancelRequested += CancelFromField;
        }

        _layer.ItemSelected += MoveLayer;
        _visible.Toggled += CommitChoice;
        _projectionMode.ItemSelected += CommitChoice;
        _material.MaterialSelected += _ => CommitChoice();
        _duplicate.Pressed += Duplicate;
        _delete.Pressed += Remove;
    }

    public MapEditorBrushId? SelectedId => _value?.Id;

    public event Action<MapEditorBrushId, MapEditorBrushDraft>? PreviewRequested;
    public event Action<MapEditorBrushId, MapEditorBrushDraft>? CommitRequested;
    public event Action<MapEditorBrushId>? CancelRequested;
    public event Action<MapEditorBrushId>? RemoveRequested;
    public event Action<MapEditorBrushId>? DuplicateRequested;
    public event Action<MapEditorBrushId, MapEditorLayer>? MoveToLayerRequested;

    public void ConfigureTextureSources(MapEditorTextureSourceRegistry textureSources)
    {
        _material.Configure(textureSources ??
                            throw new ArgumentNullException(nameof(textureSources)));
    }

    public void Apply(MapEditorBrushInspectorValue value)
    {
        _value = value;
        _applying = true;
        _title.Text = value.Brush.Name;
        _name.Apply(value.Brush.Name);
        _layer.Select((int)value.Brush.Layer);
        _visible.SetPressedNoSignal(value.Brush.Visible);
        ApplyShape(value.Brush.Shape);
        _material.Apply(value.Brush.Material);
        _materialStatus.Text = value.Brush.Material switch
        {
            MapEditorSolidColorMaterial solid => $"Color {solid.Color.Html}",
            MapEditorTextureMaterial when value.MaterialMissing =>
                "Texture missing. Choose another before saving.",
            MapEditorTextureMaterial texture => $"Texture: {Path.GetFileName(texture.Reference.Path)}",
            _ => "Material not supported",
        };
        _materialStatus.AddThemeColorOverride("font_color", value.MaterialMissing
            ? MapEditorInspectorUi.Warning
            : MapEditorInspectorUi.Success);
        _projectionMode.Select((int)value.Brush.Projection.Mode);
        _originX.Apply(value.Brush.Projection.Origin.X.ToString());
        _originY.Apply(value.Brush.Projection.Origin.Y.ToString());
        _scaleX.Apply(value.Brush.Projection.ScaleX.ToString("R"));
        _scaleY.Apply(value.Brush.Projection.ScaleY.ToString("R"));
        _projectionRotation.Apply(value.Brush.Projection.Rotation.ToString("R"));
        _diagnostic.Text = value.Diagnostic ?? string.Empty;
        UpdateProjectionAvailability();
        _applying = false;
    }

    public void CancelDraft(bool suppressFocusCommit = true)
    {
        bool dirty = _fields.Any(field => field.Dirty);
        foreach (MapEditorInspectorField field in _fields)
        {
            field.Cancel(suppressFocusCommit);
        }

        if (dirty && _value != null)
            CancelRequested?.Invoke(_value.Id);
    }

    private void ApplyShape(MapEditorBrushShape shape)
    {
        bool polygon = shape is MapEditorPolygonBrushShape;
        foreach (MapEditorInspectorField field in new[] { _x, _y, _width, _height, _rotation })
        {
            field.Visible = !polygon;
        }

        _vertices.Visible = polygon;
        switch (shape)
        {
            case MapEditorRectBrushShape rect:
                _shape.Text = "Rectangle";
                _width.Configure("Width", "Width", "Brush width");
                _height.Configure("Height", "Height", "Brush height");
                _x.Apply(rect.X.ToString());
                _y.Apply(rect.Y.ToString());
                _width.Apply(rect.Width.ToString());
                _height.Apply(rect.Height.ToString());
                _rotation.Apply(rect.Rotation.ToString("R"));
                break;
            case MapEditorEllipseBrushShape ellipse:
                _shape.Text = "Ellipse";
                _width.Configure("Width", "Radius X", "Brush radius X");
                _height.Configure("Height", "Radius Y", "Brush radius Y");
                _x.Apply(ellipse.X.ToString());
                _y.Apply(ellipse.Y.ToString());
                _width.Apply(ellipse.RadiusX.ToString());
                _height.Apply(ellipse.RadiusY.ToString());
                _rotation.Apply(ellipse.Rotation.ToString("R"));
                break;
            case MapEditorPolygonBrushShape polygonShape:
                _shape.Text = "Polygon";
                _vertices.Apply(string.Join("; ", polygonShape.Vertices.Select(point => $"{point.X},{point.Y}")));
                break;
        }
    }

    private void Preview()
    {
        if (TryRead(out MapEditorBrushDraft draft) && _value != null)
            PreviewRequested?.Invoke(_value.Id, draft);
    }

    private void Commit()
    {
        if (!TryRead(out MapEditorBrushDraft draft) || _value == null)
            return;
        foreach (MapEditorInspectorField field in _fields)
        {
            field.MarkCommitted();
        }

        CommitRequested?.Invoke(_value.Id, draft);
    }

    private void CommitChoice(long _) => CommitChoice();
    private void CommitChoice(bool _) => CommitChoice();

    private void CommitChoice()
    {
        if (_applying || _value == null || !TryRead(out MapEditorBrushDraft draft))
            return;
        UpdateProjectionAvailability();
        CommitRequested?.Invoke(_value.Id, draft);
    }

    private void MoveLayer(long index)
    {
        if (!_applying && _value != null && index != (int)_value.Brush.Layer)
        {
            CancelDraft();
            MoveToLayerRequested?.Invoke(_value.Id, (MapEditorLayer)index);
        }
    }

    private void Duplicate()
    {
        if (_value == null)
            return;
        CancelDraft();
        DuplicateRequested?.Invoke(_value.Id);
    }

    private void Remove()
    {
        if (_value == null)
            return;
        CancelDraft();
        RemoveRequested?.Invoke(_value.Id);
    }

    private void CancelFromField()
    {
        foreach (MapEditorInspectorField field in _fields)
        {
            field.Cancel(true);
        }

        if (_value != null)
            CancelRequested?.Invoke(_value.Id);
    }

    private bool TryRead(out MapEditorBrushDraft draft)
    {
        draft = null!;
        if (_value == null)
            return false;
        ClearErrors();
        MapEditorBrushShape shape;
        MapEditorPoint movement = default;
        if (_value.Brush.Shape is MapEditorPolygonBrushShape)
        {
            if (!TryParseVertices(_vertices.Editor.Text, out ImmutableArray<MapEditorPoint> vertices,
                    out string? error) || !MapEditorBrushValidator.TryValidatePolygon(vertices, out error))
            {
                _vertices.SetError(error);
                return false;
            }

            shape = new MapEditorPolygonBrushShape(vertices);
        }
        else
        {
            if (!Integer(_x, out int x) || !Integer(_y, out int y) ||
                !PositiveInteger(_width, out int width) || !PositiveInteger(_height, out int height) ||
                !FiniteFloat(_rotation, out float rotation))
                return false;
            switch (_value.Brush.Shape)
            {
                case MapEditorRectBrushShape rect:
                    movement = new MapEditorPoint(checked(x - rect.X), checked(y - rect.Y));
                    shape = new MapEditorRectBrushShape(x, y, width, height, rotation);
                    break;
                case MapEditorEllipseBrushShape ellipse:
                    movement = new MapEditorPoint(checked(x - ellipse.X), checked(y - ellipse.Y));
                    shape = new MapEditorEllipseBrushShape(x, y, width, height, rotation);
                    break;
                default:
                    return false;
            }
        }

        if (!Integer(_originX, out int originX) || !Integer(_originY, out int originY) ||
            !PositiveFloat(_scaleX, out float scaleX) || !PositiveFloat(_scaleY, out float scaleY) ||
            !FiniteFloat(_projectionRotation, out float projectionRotation))
            return false;
        MapEditorTextureProjection oldProjection = _value.Brush.Projection;
        MapEditorPoint origin = movement == default
            ? new MapEditorPoint(originX, originY)
            : new MapEditorPoint(checked(oldProjection.Origin.X + movement.X),
                checked(oldProjection.Origin.Y + movement.Y));
        draft = new MapEditorBrushDraft(_name.Editor.Text.Trim(), _value.Brush.Layer, shape,
            _material.SelectedMaterial, new MapEditorTextureProjection(
                (MapEditorProjectionMode)_projectionMode.Selected,
                origin, scaleX, scaleY, projectionRotation), _visible.ButtonPressed);
        return true;
    }

    private void UpdateProjectionAvailability()
    {
        bool repeat = _material.SelectedMaterial is MapEditorTextureMaterial &&
                      _projectionMode.Selected == (int)MapEditorProjectionMode.REPEAT;
        foreach (MapEditorInspectorField field in new[] { _originX, _originY, _scaleX, _scaleY, _projectionRotation })
        {
            field.Editor.Editable = repeat;
            field.Modulate = repeat ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
    }

    private void ClearErrors()
    {
        foreach (MapEditorInspectorField field in _fields)
        {
            field.SetError(null);
        }
    }

    private static bool Integer(MapEditorInspectorField field, out int value)
    {
        if (int.TryParse(field.Editor.Text, out value))
            return true;
        field.SetError("Enter a whole number.");
        return false;
    }

    private static bool PositiveInteger(MapEditorInspectorField field, out int value)
    {
        if (Integer(field, out value) && value > 0)
            return true;
        field.SetError("Enter a whole number above zero.");
        return false;
    }

    private static bool FiniteFloat(MapEditorInspectorField field, out float value)
    {
        if (float.TryParse(field.Editor.Text, out value) && float.IsFinite(value))
            return true;
        field.SetError("Enter a number.");
        return false;
    }

    private static bool PositiveFloat(MapEditorInspectorField field, out float value)
    {
        if (FiniteFloat(field, out value) && value > 0)
            return true;
        field.SetError("Enter a number above zero.");
        return false;
    }

    public static bool TryParseVertices(string text, out ImmutableArray<MapEditorPoint> vertices,
        out string? error)
    {
        ImmutableArray<MapEditorPoint>.Builder parsed = ImmutableArray.CreateBuilder<MapEditorPoint>();
        foreach (string entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries |
                                                 StringSplitOptions.TrimEntries))
        {
            string[] coordinates = entry.Split(',', StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2 || !int.TryParse(coordinates[0], out int x) ||
                !int.TryParse(coordinates[1], out int y))
            {
                vertices = [];
                error = "Enter points as x,y; x,y; x,y.";
                return false;
            }

            parsed.Add(new MapEditorPoint(x, y));
        }

        vertices = parsed.ToImmutable();
        error = null;
        return true;
    }
}
