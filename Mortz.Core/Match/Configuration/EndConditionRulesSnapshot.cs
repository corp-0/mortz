namespace Mortz.Core.Match.Configuration;

public abstract record EndConditionRulesSnapshot
{
    public abstract EndConditionRules ToMutable();
}

public static class EndConditionRulesProjection
{
    public static EndConditionRulesSnapshot ToSnapshot(EndConditionRules rules) => rules.ToSnapshot();

    public static EndConditionRules ToMutable(EndConditionRulesSnapshot snapshot) => snapshot.ToMutable();

    public static void Clamp(EndConditionRules rules) => EndConditionRulesMetadata.Clamp(rules);

}
