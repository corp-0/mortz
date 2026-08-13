using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed class VictoryRuleDescriptor(
    string id,
    string displayName,
    Type rulesType,
    IReadOnlyList<UiCategoryDescriptor> categories,
    Func<VictoryRules> createDefault,
    Action<VictoryRules> clamp,
    Func<VictoryRules, byte[]> serialize,
    Func<byte[], VictoryRules> deserialize)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type RulesType { get; } = rulesType;
    public IReadOnlyList<UiCategoryDescriptor> Categories { get; } = categories;

    public VictoryRules CreateDefault() => createDefault();

    internal void Clamp(VictoryRules rules) => clamp(rules);

    internal byte[] Serialize(VictoryRules rules) => serialize(rules);

    internal VictoryRules Deserialize(byte[] data) => deserialize(data);
}
