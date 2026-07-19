namespace Mortz.Core.Ui;

public sealed class UiCategoryDescriptor(
    string displayName,
    IReadOnlyList<IUiPropertyDescriptor> properties)
{
    public string DisplayName { get; } = displayName;
    public IReadOnlyList<IUiPropertyDescriptor> Properties { get; } = properties;
}
