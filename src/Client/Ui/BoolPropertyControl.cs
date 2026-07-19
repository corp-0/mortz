using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

public partial class BoolPropertyControl : HBoxContainer, IUiPropertyControl
{
    [Export] private Label _label = null!;
    [Export] private CheckButton _value = null!;

    private IUiPropertyDescriptor _descriptor = null!;
    private object _model = null!;
    private Action _changed = null!;
    private bool _updating;

    public override void _Ready() => _value.Toggled += OnToggled;

    public override void _ExitTree() => _value.Toggled -= OnToggled;

    public void Bind(IUiPropertyDescriptor descriptor, object model, Action changed)
    {
        _descriptor = descriptor;
        _model = model;
        _changed = changed;
        _label.Text = descriptor.DisplayName;
        Refresh();
    }

    public void UpdateModel(object model)
    {
        _model = model;
        Refresh();
    }

    public void SetEditable(bool editable) => _value.Disabled = !editable;

    private void Refresh()
    {
        _updating = true;
        _value.ButtonPressed = (bool)_descriptor.GetValue(_model)!;
        _updating = false;
    }

    private void OnToggled(bool value)
    {
        if (_updating)
            return;
        _descriptor.SetValue(_model, value);
        _changed();
    }
}
