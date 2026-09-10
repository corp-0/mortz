using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("fixed", "Fixed", typeof(FixedRespawnRules))]
[ConfigVariant("none", "None", typeof(NoRespawnRules))]
public abstract class RespawnRules
{
    public abstract RespawnRulesSnapshot ToSnapshot();
}

public partial class FixedRespawnRules : RespawnRules
{
    [UiCategory("Respawn")]
    [UiProperty("Respawn Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float Delay { get; set; } = SimConfig.RESPAWN_DELAY;

    public int DelayTicks => (int)(Delay * SimConfig.TICK_RATE);
}

public partial class NoRespawnRules : RespawnRules;

public abstract record RespawnRulesSnapshot
{
    public abstract RespawnRules ToMutable();
}

public static class RespawnRulesProjection
{
    public static RespawnRulesSnapshot ToSnapshot(RespawnRules rules) => rules.ToSnapshot();
    public static RespawnRules ToMutable(RespawnRulesSnapshot snapshot) => snapshot.ToMutable();
    public static void Clamp(RespawnRules rules) => RespawnRulesMetadata.Clamp(rules);
}
