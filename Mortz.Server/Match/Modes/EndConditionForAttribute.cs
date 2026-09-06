namespace Mortz.Server.Match.Modes;

/// <summary>Registers an end-condition component for one authored rule type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EndConditionForAttribute(Type rulesType) : Attribute
{
    public Type RulesType { get; } = rulesType;
}
