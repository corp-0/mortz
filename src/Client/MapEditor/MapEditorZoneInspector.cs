using System.Collections.Immutable;
using Godot;
using Mortz.Content;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorZoneInspectorValue(MapEditorZoneId Id, MapEditorZoneDraft Zone);

public partial class MapEditorZoneInspector : ScrollContainer
{
    [Export] private Label _title = null!;
    [Export] private MapEditorInspectorField _name = null!;
    [Export] private Label _shape = null!;
    [Export] private MapEditorInspectorField _x = null!;
    [Export] private MapEditorInspectorField _y = null!;
    [Export] private MapEditorInspectorField _sizeA = null!;
    [Export] private MapEditorInspectorField _sizeB = null!;
    [Export] private MapEditorInspectorField _rotation = null!;
    [Export] private MapEditorInspectorField _tags = null!;
    [Export] private VBoxContainer _effects = null!;
    [Export] private Label _diagnostic = null!;
    [Export] private Button _addEffect = null!;
    [Export] private Button _delete = null!;
    private MapEditorInspectorField[] _fields = null!;
    private PackedScene? _effectRowScene;
    private MapEditorZoneInspectorValue? _value;
    private bool _applying;

    public override void _Ready()
    {
        _name.Configure("Name", "Name", "Zone name");
        _x.Configure("X", "X", "Zone X position");
        _y.Configure("Y", "Y", "Zone Y position");
        _rotation.Configure("Rotation", "Rotation", "Zone rotation");
        _tags.Configure("Tags", "Tags", "Zone tags");
        _fields = [_name, _x, _y, _sizeA, _sizeB, _rotation, _tags];
        foreach (MapEditorInspectorField field in _fields)
        {
            field.PreviewRequested += Preview;
            field.CommitRequested += Commit;
            field.CancelRequested += CancelFromField;
        }

        _addEffect.Pressed += AddEffect;
        _delete.Pressed += Remove;
    }

    public MapEditorZoneId? SelectedId => _value?.Id;

    public event Action<MapEditorZoneId, MapEditorZoneDraft>? PreviewRequested;
    public event Action<MapEditorZoneId, MapEditorZoneDraft>? CommitRequested;
    public event Action<MapEditorZoneId>? CancelRequested;
    public event Action<MapEditorZoneId>? RemoveRequested;

    public void Configure(PackedScene effectRowScene) => _effectRowScene = effectRowScene;

    public void Apply(MapEditorZoneInspectorValue value)
    {
        _value = value;
        _applying = true;
        _title.Text = value.Zone.Shape switch
        {
            RectMapZoneShape => "Rectangle zone",
            EllipseMapZoneShape => "Ellipse zone",
            _ => "Circle zone",
        };
        _name.Apply(value.Zone.Name);
        _tags.Apply(string.Join(", ", value.Zone.Tags));
        _x.Apply(value.Zone.Shape.X.ToString());
        _y.Apply(value.Zone.Shape.Y.ToString());
        switch (value.Zone.Shape)
        {
            case RectMapZoneShape rect:
                _shape.Text = "Rectangle";
                _sizeA.Configure("SizeA", "Width", "Zone width");
                _sizeB.Configure("SizeB", "Height", "Zone height");
                _sizeA.Apply(rect.Width.ToString());
                _sizeB.Apply(rect.Height.ToString());
                _sizeB.Show();
                _rotation.Apply(rect.Rotation.ToString("R"));
                _rotation.Show();
                break;
            case EllipseMapZoneShape ellipse:
                _shape.Text = "Ellipse";
                _sizeA.Configure("SizeA", "Radius X", "Zone radius X");
                _sizeB.Configure("SizeB", "Radius Y", "Zone radius Y");
                _sizeA.Apply(ellipse.RadiusX.ToString());
                _sizeB.Apply(ellipse.RadiusY.ToString());
                _sizeB.Show();
                _rotation.Apply(ellipse.Rotation.ToString("R"));
                _rotation.Show();
                break;
            case CircleMapZoneShape circle:
                _shape.Text = "Circle";
                _sizeA.Configure("SizeA", "Radius", "Zone radius");
                _sizeA.Apply(circle.Radius.ToString());
                _sizeB.Hide();
                _rotation.Hide();
                break;
        }

        if (!HasFocusedEffect())
            ApplyEffects(value.Zone.Effects);
        _diagnostic.Text = string.Empty;
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

    private void Remove()
    {
        if (_value == null)
            return;
        CancelDraft();
        RemoveRequested?.Invoke(_value.Id);
    }

    private void Preview()
    {
        if (TryRead(out MapEditorZoneDraft draft) && _value != null)
            PreviewRequested?.Invoke(_value.Id, draft);
    }

    private void Commit()
    {
        if (!TryRead(out MapEditorZoneDraft draft) || _value == null)
            return;
        foreach (MapEditorInspectorField field in _fields)
        {
            field.MarkCommitted();
        }

        CommitRequested?.Invoke(_value.Id, draft);
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

    private bool TryRead(out MapEditorZoneDraft draft)
    {
        draft = null!;
        if (_value == null)
            return false;
        foreach (MapEditorInspectorField field in _fields)
        {
            field.SetError(null);
        }

        if (!Integer(_x, out int x) || !Integer(_y, out int y) ||
            !PositiveInteger(_sizeA, out int sizeA))
            return false;
        MapZoneShape shape;
        switch (_value.Zone.Shape)
        {
            case RectMapZoneShape:
                if (!PositiveInteger(_sizeB, out int height) || !FiniteFloat(_rotation, out float rectRotation))
                    return false;
                shape = new RectMapZoneShape(x, y, sizeA, height, rectRotation);
                break;
            case EllipseMapZoneShape:
                if (!PositiveInteger(_sizeB, out int radiusY) || !FiniteFloat(_rotation, out float ellipseRotation))
                    return false;
                shape = new EllipseMapZoneShape(x, y, sizeA, radiusY, ellipseRotation);
                break;
            default:
                shape = new CircleMapZoneShape(x, y, sizeA);
                break;
        }

        string[] tags = _tags.Editor.Text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                                     StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        draft = new MapEditorZoneDraft(_name.Editor.Text.Trim(), [.. tags], shape, ReadEffects());
        return true;
    }

    private void AddEffect()
    {
        if (_value == null)
            return;
        CancelDraft();
        AddEffectRow(new MapZoneEffect(Stat.GRAVITY, StatOp.MUL, 1));
        CommitEffects();
    }

    private void ApplyEffects(ImmutableArray<MapZoneEffect> effects)
    {
        foreach (Node child in _effects.GetChildren())
        {
            _effects.RemoveChild(child);
            child.QueueFree();
        }

        foreach (MapZoneEffect effect in effects)
        {
            AddEffectRow(effect);
        }
    }

    private void AddEffectRow(MapZoneEffect effect)
    {
        if (_effectRowScene == null)
            return;
        ZoneEffectRow row = _effectRowScene.Instantiate<ZoneEffectRow>();
        _effects.AddChild(row);
        row.Bind(effect);
        row.Changed += CommitEffects;
        row.RemoveRequested += RemoveEffect;
    }

    private void RemoveEffect(ZoneEffectRow row)
    {
        _effects.RemoveChild(row);
        row.QueueFree();
        CommitEffects();
    }

    private void CommitEffects()
    {
        if (_applying || _value == null || !TryRead(out MapEditorZoneDraft draft))
            return;
        CommitRequested?.Invoke(_value.Id, draft);
    }

    private ImmutableArray<MapZoneEffect> ReadEffects() =>
        [.. _effects.GetChildren().OfType<ZoneEffectRow>().Select(row => row.Value)];

    private bool HasFocusedEffect()
    {
        Control? focus = GetViewport()?.GuiGetFocusOwner();
        return focus != null && _effects.IsAncestorOf(focus);
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
}
