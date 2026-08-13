namespace Mortz.Core.Match.Configuration;

/// <summary>Declares one selectable victory-rule variant.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VictoryRuleCaseAttribute(
    string id,
    string displayName,
    Type type) : Attribute
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type Type { get; } = type;
}
