namespace Mortz.Server.Match.Scoring.WinConditions;

/// <summary>Registers a strategy for one victory-rule type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VictoryRuleStrategyAttribute(Type rulesType) : Attribute
{
    public Type RulesType { get; } = rulesType;
}
