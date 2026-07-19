using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

public partial class FloatPropertyControl : HBoxContainer, IUiPropertyControl
{
    [Export] private Label _label = null!;
    [Export] private SpinBox _value = null!;

    private IUiPropertyDescriptor _descriptor = null!;
    private object _model = null!;
    private Action _changed = null!;
    private bool _updating;

    public override void _Ready() => _value.ValueChanged += OnValueChanged;

    public override void _ExitTree() => _value.ValueChanged -= OnValueChanged;

    public void Bind(IUiPropertyDescriptor descriptor, object model, Action changed)
    {
        _descriptor = descriptor;
        _model = model;
        _changed = changed;
        _label.Text = descriptor.DisplayName;
        _value.ApplyRangeHints(descriptor);
        Refresh();
    }

    public void UpdateModel(object model)
    {
        _model = model;
        Refresh();
    }

    public void SetEditable(bool editable) => _value.Editable = editable;

    private void Refresh()
    {
        _updating = true;
        _value.Value = (float)_descriptor.GetValue(_model)!;
        _updating = false;
    }

    private void OnValueChanged(double value)
    {
        if (_updating)
            return;
        _descriptor.SetValue(_model, (float)value);
        _changed();
    }
}
