using Godot;
using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Renders a [UiProperty]-decorated object as category blocks with
/// a control row per property.</summary>
[Tool]
public partial class UiPropertySheet : VBoxContainer
{
    private const int CATEGORY_GAP = 22;

    private sealed record BoundProperty(
        IUiPropertyDescriptor Descriptor,
        IUiPropertyControl Control,
        Control Row);

    private sealed record BoundCategory(
        Control Container,
        List<BoundProperty> Properties);

    [Export] private PackedScene _boolControl = null!;
    [Export] private PackedScene _intControl = null!;
    [Export] private PackedScene _floatControl = null!;
    [Export] private PackedScene _enumControl = null!;

    private readonly List<BoundCategory> _categories = [];
    private object? _model;

    public Type? BoundModelType { get; private set; }

    internal int ControlCount => _categories.Sum(category => category.Properties.Count);
    internal int CategoryBlockCount { get; private set; }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            Build(SheetPreviewModelUiMetadata.Categories, new SheetPreviewModel(), static () => { });
    }

    public void Build(IReadOnlyList<UiCategoryDescriptor> categories, object model,
        Action changed)
    {
        _model = model;
        BoundModelType = model.GetType();
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
            List<BoundProperty> properties = [];
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
                if (node is not IUiPropertyControl control || node is not Control row)
                {
                    node.Free();
                    continue;
                }
                control.Bind(descriptor, model, () =>
                {
                    RefreshVisibility();
                    changed();
                });
                properties.Add(new BoundProperty(descriptor, control, row));
                categoryBlock.AddChild(node);
            }

            AddChild(categoryMargin);
            _categories.Add(new BoundCategory(categoryMargin, properties));
            categoryIndex++;
        }
        CategoryBlockCount = categoryIndex;
        RefreshVisibility();
    }

    public void UpdateModel(object model)
    {
        _model = model;
        foreach (BoundProperty property in _categories.SelectMany(category => category.Properties))
        {
            property.Control.UpdateModel(model);
        }
        RefreshVisibility();
    }

    public void SetEditable(bool editable)
    {
        foreach (BoundProperty property in _categories.SelectMany(category => category.Properties))
        {
            property.Control.SetEditable(editable);
        }
    }

    private void RefreshVisibility()
    {
        if (_model == null)
            return;
        foreach (BoundCategory category in _categories)
        {
            bool anyVisible = false;
            foreach (BoundProperty property in category.Properties)
            {
                property.Row.Visible = property.Descriptor.IsVisible(_model);
                anyVisible |= property.Row.Visible;
            }
            category.Container.Visible = anyVisible;
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
