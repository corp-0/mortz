using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[VictoryRuleCase("kills", "Kills", typeof(KillsVictoryRules))]
[VictoryRuleCase("kill_lead", "Kill Lead", typeof(KillLeadVictoryRules))]
public abstract class VictoryRules;

public sealed partial class KillsVictoryRules : VictoryRules
{
    [UiCategory("Victory")]
    [UiProperty("Kill Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = SimConfig.KILL_TARGET;
}

public sealed partial class KillLeadVictoryRules : VictoryRules
{
    [UiCategory("Victory")]
    [UiProperty("Kill Lead Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = SimConfig.KILL_LEAD_TARGET;
}
