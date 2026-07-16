using Godot;
using Mortz.Core.Match;
using Mortz.Core.Ui;

namespace Mortz.Client.Menus;

public partial class EnumRuleControl : HBoxContainer, IMatchRuleControl
{
    [Export] private Label _label = null!;
    [Export] private OptionButton _value = null!;

    private readonly List<object> _options = [];
    private IUiPropertyDescriptor<MatchConfig> _descriptor = null!;
    private MatchConfig _config = null!;
    private Action _changed = null!;
    private bool _updating;

    public override void _Ready() => _value.ItemSelected += OnItemSelected;

    public override void _ExitTree() => _value.ItemSelected -= OnItemSelected;

    public void Bind(IUiPropertyDescriptor<MatchConfig> descriptor, MatchConfig config,
        Action changed)
    {
        _descriptor = descriptor;
        _config = config;
        _changed = changed;
        _label.Text = descriptor.DisplayName;
        _value.Clear();
        _options.Clear();
        foreach (object option in Enum.GetValues(descriptor.ValueType))
        {
            _options.Add(option);
            _value.AddItem(Humanize(option.ToString() ?? ""));
        }
        Refresh();
    }

    public void UpdateConfig(MatchConfig config)
    {
        _config = config;
        Refresh();
    }

    public void SetEditable(bool editable) => _value.Disabled = !editable;

    private void Refresh()
    {
        object current = _descriptor.GetValue(_config)!;
        int index = _options.FindIndex(option => Equals(option, current));
        _updating = true;
        _value.Select(Math.Max(0, index));
        _updating = false;
    }

    private void OnItemSelected(long index)
    {
        if (_updating || index < 0 || index >= _options.Count)
            return;
        _descriptor.SetValue(_config, _options[(int)index]);
        _changed();
    }

    private static string Humanize(string value) =>
        string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}
