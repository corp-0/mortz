using Godot;
using Mortz.Core.Match.Configuration;

namespace Mortz.Client.Ui;

/// <summary>Selects and edits any declared victory-rule variant.</summary>
[Tool]
public partial class VictoryRulesSheet : VBoxContainer
{
    [Export] private OptionButton _variant = null!;
    [Export] private UiPropertySheet _properties = null!;

    private Action<VictoryRules> _changed = null!;
    private VictoryRules _rules = null!;
    private bool _updating;

    public VictoryRules Rules => _rules;

    public override void _Ready()
    {
        _variant.ItemSelected += OnVariantSelected;
        PopulateVariants();
        if (Engine.IsEditorHint())
            Build(new KillsVictoryRules(), static _ => { });
    }

    public override void _ExitTree() => _variant.ItemSelected -= OnVariantSelected;

    public void Build(VictoryRules rules, Action<VictoryRules> changed)
    {
        _changed = changed;
        UpdateModel(rules);
    }

    public void UpdateModel(VictoryRules rules)
    {
        _rules = rules;
        VictoryRuleDescriptor descriptor = VictoryRulesMetadata.For(rules);
        int index = Enumerable.Range(0, VictoryRulesMetadata.Variants.Count)
            .Single(candidate => VictoryRulesMetadata.Variants[candidate] == descriptor);
        _updating = true;
        _variant.Select(index);
        _updating = false;
        _properties.Build(
            descriptor.Categories,
            rules,
            () => _changed(_rules),
            showFirstCategoryHeading: false);
    }

    public void SetEditable(bool editable)
    {
        _variant.Disabled = !editable;
        _properties.SetEditable(editable);
    }

    private void PopulateVariants()
    {
        _variant.Clear();
        foreach (VictoryRuleDescriptor descriptor in VictoryRulesMetadata.Variants)
        {
            _variant.AddItem(descriptor.DisplayName);
        }
    }

    private void OnVariantSelected(long index)
    {
        if (_updating || index < 0 || index >= VictoryRulesMetadata.Variants.Count)
            return;
        VictoryRules rules = VictoryRulesMetadata.Variants[(int)index].CreateDefault();
        UpdateModel(rules);
        _changed(rules);
    }
}
