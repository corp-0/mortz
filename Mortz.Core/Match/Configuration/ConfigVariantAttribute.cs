namespace Mortz.Core.Match.Configuration;

/// <summary>Declares one selectable configuration variant.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class ConfigVariantAttribute(
    string id,
    string displayName,
    Type type) : Attribute
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public Type Type { get; } = type;
}
