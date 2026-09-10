namespace Mortz.Core.Match.Configuration;

[ConfigVariant("none", "None", typeof(NoObjectiveRules))]
public abstract class ObjectiveRules
{
    public abstract ObjectiveRulesSnapshot ToSnapshot();
}

public partial class NoObjectiveRules : ObjectiveRules;

public abstract record ObjectiveRulesSnapshot
{
    public abstract ObjectiveRules ToMutable();
}

public static class ObjectiveRulesProjection
{
    public static ObjectiveRulesSnapshot ToSnapshot(ObjectiveRules rules) => rules.ToSnapshot();
    public static ObjectiveRules ToMutable(ObjectiveRulesSnapshot snapshot) => snapshot.ToMutable();
    public static void Clamp(ObjectiveRules rules) => ObjectiveRulesMetadata.Clamp(rules);
}
