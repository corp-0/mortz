using Godot;
using Mortz.Core.Match.Configuration;

namespace Mortz.Client.Ui;

[Tool]
[GlobalClass]
public partial class ConfigVariantSheet : VBoxContainer
{
    public enum RuleFamily { END_CONDITION, OBJECTIVE, RESPAWN }

    [Export] public RuleFamily Family { get; set; }
    [Export] private OptionButton _variant = null!;
    [Export] private UiPropertySheet _properties = null!;

    private Action<object> _changed = static _ => { };
    private object _rules = null!;
    private bool _updating;

    public object Rules => _rules;

    private IReadOnlyList<IConfigVariantDescriptor> Variants => Family switch
    {
        RuleFamily.END_CONDITION => EndConditionRulesMetadata.Variants,
        RuleFamily.OBJECTIVE => ObjectiveRulesMetadata.Variants,
        RuleFamily.RESPAWN => RespawnRulesMetadata.Variants,
        _ => throw new ArgumentOutOfRangeException(nameof(Family)),
    };

    public override void _Ready()
    {
        _variant.ItemSelected += OnVariantSelected;
        if (Engine.IsEditorHint())
        {
            UpdateModel(Variants[0].CreateDefault());
        }
    }

    public override void _ExitTree() => _variant.ItemSelected -= OnVariantSelected;

    public void Build<T>(T rules, Action<T> changed) where T : class
    {
        _changed = value => changed((T)value);
        UpdateModel(rules);
    }

    public void UpdateModel(object rules)
    {
        IReadOnlyList<IConfigVariantDescriptor> variants = Variants;
        int index = Enumerable.Range(0, variants.Count)
            .Single(candidate => variants[candidate].RulesType == rules.GetType());
        _rules = rules;
        IConfigVariantDescriptor descriptor = variants[index];
        _updating = true;
        _variant.Clear();
        foreach (IConfigVariantDescriptor variant in variants)
        {
            _variant.AddItem(variant.DisplayName);
        }
        _variant.Select(index);
        _updating = false;
        _properties.Build(descriptor.Categories, rules, () => _changed(_rules),
            showFirstCategoryHeading: false);
    }

    public void SetEditable(bool editable)
    {
        _variant.Disabled = !editable;
        _properties.SetEditable(editable);
    }

    private void OnVariantSelected(long index)
    {
        if (_updating || index < 0 || index >= Variants.Count)
        {
            return;
        }
        object rules = Variants[(int)index].CreateDefault();
        UpdateModel(rules);
        _changed(rules);
    }
}
