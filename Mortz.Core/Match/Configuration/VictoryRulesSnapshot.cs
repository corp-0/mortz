namespace Mortz.Core.Match.Configuration;

public abstract record VictoryRulesSnapshot
{
    public abstract VictoryRules ToMutable();
}

public static class VictoryRulesProjection
{
    public static VictoryRulesSnapshot ToSnapshot(VictoryRules rules) => rules.ToSnapshot();

    public static VictoryRules ToMutable(VictoryRulesSnapshot snapshot) => snapshot.ToMutable();

    public static void Clamp(VictoryRules rules) => VictoryRulesMetadata.Clamp(rules);

    public static byte[] ToBytes(VictoryRules rules) => VictoryRulesMetadata.Serialize(rules);

    public static VictoryRules FromBytes(byte[] data) => VictoryRulesMetadata.Deserialize(data);
}
