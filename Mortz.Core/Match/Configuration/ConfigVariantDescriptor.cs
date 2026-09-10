using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public interface IConfigVariantDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    Type RulesType { get; }
    IReadOnlyList<UiCategoryDescriptor> Categories { get; }
    object CreateDefault();
}

public class ConfigVariantDescriptor<T>(
    string id,
    string displayName,
    Type rulesType,
    IReadOnlyList<UiCategoryDescriptor> categories,
    Func<T> createDefault,
    Action<T> clamp) : IConfigVariantDescriptor where T : class
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type RulesType { get; } = rulesType;
    public IReadOnlyList<UiCategoryDescriptor> Categories { get; } = categories;

    public T CreateDefault() => createDefault();
    object IConfigVariantDescriptor.CreateDefault() => CreateDefault();
    public void Clamp(T rules) => clamp(rules);
}
