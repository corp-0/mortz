using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed class EndConditionDescriptor(
    string id,
    string displayName,
    Type rulesType,
    IReadOnlyList<UiCategoryDescriptor> categories,
    Func<EndConditionRules> createDefault,
    Action<EndConditionRules> clamp)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type RulesType { get; } = rulesType;
    public IReadOnlyList<UiCategoryDescriptor> Categories { get; } = categories;

    public EndConditionRules CreateDefault() => createDefault();

    internal void Clamp(EndConditionRules rules) => clamp(rules);

}
