using Mortz.Core;
using Mortz.Core.Match;
using Mortz.Core.Ui;

namespace Mortz.Client;

/// <summary>Common binding surface implemented by type-specific rule prefabs.</summary>
internal interface IMatchRuleControl
{
    void Bind(IUiPropertyDescriptor<MatchConfig> descriptor, MatchConfig config,
        Action changed);
    void UpdateConfig(MatchConfig config);
    void SetEditable(bool editable);
}
