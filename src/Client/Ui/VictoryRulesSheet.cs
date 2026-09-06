using Godot;
using Mortz.Core.Match.Configuration;

namespace Mortz.Client.Ui;

/// <summary>Selects and edits the mode's end condition.</summary>
[Tool]
public partial class VictoryRulesSheet : VBoxContainer
{
    [Export] private OptionButton _variant = null!;
    [Export] private UiPropertySheet _properties = null!;

    private Action<EndConditionRules> _changed = null!;
    private EndConditionRules _rules = null!;
    private bool _updating;

    public EndConditionRules Rules => _rules;

    public override void _Ready()
    {
        _variant.ItemSelected += OnVariantSelected;
        PopulateVariants();
        if (Engine.IsEditorHint())
            Build(new ScoreTargetRules(), static _ => { });
    }

    public override void _ExitTree() => _variant.ItemSelected -= OnVariantSelected;

    public void Build(EndConditionRules rules, Action<EndConditionRules> changed)
    {
        _changed = changed;
        UpdateModel(rules);
    }

    public void UpdateModel(EndConditionRules rules)
    {
        _rules = rules;
        EndConditionDescriptor descriptor = EndConditionRulesMetadata.For(rules);
        int index = Enumerable.Range(0, EndConditionRulesMetadata.Variants.Count)
            .Single(candidate => EndConditionRulesMetadata.Variants[candidate] == descriptor);
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
        foreach (EndConditionDescriptor descriptor in EndConditionRulesMetadata.Variants)
        {
            _variant.AddItem(descriptor.DisplayName);
        }
    }

    private void OnVariantSelected(long index)
    {
        if (_updating || index < 0 || index >= EndConditionRulesMetadata.Variants.Count)
            return;
        EndConditionRules rules = EndConditionRulesMetadata.Variants[(int)index].CreateDefault();
        UpdateModel(rules);
        _changed(rules);
    }
}
