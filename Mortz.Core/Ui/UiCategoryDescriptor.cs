namespace Mortz.Core.Ui;

public sealed class UiCategoryDescriptor<TModel>(
    string displayName,
    IReadOnlyList<IUiPropertyDescriptor<TModel>> properties)
{
    public string DisplayName { get; } = displayName;
    public IReadOnlyList<IUiPropertyDescriptor<TModel>> Properties { get; } = properties;
}
