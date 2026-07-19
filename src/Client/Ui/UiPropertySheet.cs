using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Renders a [UiProperty]-decorated object as category blocks with
/// a control row per property.</summary>
public partial class UiPropertySheet : VBoxContainer
{
    private const int CATEGORY_GAP = 22;

    [Export] private PackedScene _boolControl = null!;
    [Export] private PackedScene _intControl = null!;
    [Export] private PackedScene _floatControl = null!;
    [Export] private PackedScene _enumControl = null!;

    private readonly List<IUiPropertyControl> _controls = [];

    internal int ControlCount => _controls.Count;
    internal int CategoryBlockCount { get; private set; }

    public void Build(IReadOnlyList<UiCategoryDescriptor> categories, object model,
        Action changed)
    {
        int categoryIndex = 0;
        foreach (UiCategoryDescriptor category in categories)
        {
            MarginContainer categoryMargin = new();
            categoryMargin.AddThemeConstantOverride(
                "margin_top", categoryIndex == 0 ? 0 : CATEGORY_GAP);
            categoryMargin.AddThemeConstantOverride("margin_bottom", 6);
            VBoxContainer categoryBlock = new();
            categoryBlock.AddThemeConstantOverride("separation", 7);
            categoryMargin.AddChild(categoryBlock);

            Label heading = new() { Text = category.DisplayName };
            heading.AddThemeFontSizeOverride("font_size", 18);
            heading.AddThemeColorOverride("font_color", new Color("cbd5e1"));
            categoryBlock.AddChild(heading);
            categoryBlock.AddChild(new HSeparator());
            foreach (IUiPropertyDescriptor descriptor in category.Properties)
            {
                PackedScene? scene = ControlScene(descriptor.ValueType);
                if (scene == null)
                {
                    categoryBlock.AddChild(new Label
                    {
                        Text = $"{descriptor.DisplayName}: unsupported {descriptor.ValueType.Name}",
                    });
                    continue;
                }
                Node node = scene.Instantiate();
                if (node is not IUiPropertyControl control)
                {
                    node.Free();
                    continue;
                }
                control.Bind(descriptor, model, changed);
                _controls.Add(control);
                categoryBlock.AddChild(node);
            }

            AddChild(categoryMargin);
            categoryIndex++;
        }
        CategoryBlockCount = categoryIndex;
    }

    public void UpdateModel(object model)
    {
        foreach (IUiPropertyControl control in _controls)
        {
            control.UpdateModel(model);
        }
    }

    public void SetEditable(bool editable)
    {
        foreach (IUiPropertyControl control in _controls)
        {
            control.SetEditable(editable);
        }
    }

    private PackedScene? ControlScene(Type valueType)
    {
        if (valueType == typeof(bool))
            return _boolControl;
        if (valueType == typeof(int))
            return _intControl;
        if (valueType == typeof(float))
            return _floatControl;
        return valueType.IsEnum ? _enumControl : null;
    }
}
