using Godot;
using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Ui;

namespace Mortz.Client;

public partial class FloatRuleControl : HBoxContainer, IMatchRuleControl
{
    [Export] private Label _label = null!;
    [Export] private SpinBox _value = null!;

    private IUiPropertyDescriptor<MatchConfig> _descriptor = null!;
    private MatchConfig _config = null!;
    private Action _changed = null!;
    private bool _updating;

    public override void _Ready() => _value.ValueChanged += OnValueChanged;

    public override void _ExitTree() => _value.ValueChanged -= OnValueChanged;

    public void Bind(IUiPropertyDescriptor<MatchConfig> descriptor, MatchConfig config,
        Action changed)
    {
        _descriptor = descriptor;
        _config = config;
        _changed = changed;
        _label.Text = descriptor.DisplayName;
        Refresh();
    }

    public void UpdateConfig(MatchConfig config)
    {
        _config = config;
        Refresh();
    }

    public void SetEditable(bool editable) => _value.Editable = editable;

    private void Refresh()
    {
        _updating = true;
        _value.Value = (float)_descriptor.GetValue(_config)!;
        _updating = false;
    }

    private void OnValueChanged(double value)
    {
        if (_updating)
            return;
        _descriptor.SetValue(_config, (float)value);
        _changed();
    }
}
