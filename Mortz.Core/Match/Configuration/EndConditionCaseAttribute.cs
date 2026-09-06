namespace Mortz.Core.Match.Configuration;

/// <summary>Declares one selectable end-condition rule.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EndConditionCaseAttribute(
    string id,
    string displayName,
    Type type) : Attribute
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type Type { get; } = type;
}
