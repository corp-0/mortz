namespace Mortz.Core.Match.Configuration;

/// <summary>Includes a nested value through an explicit snapshot and wire projection.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigValueAttribute(Type snapshotType, Type projectionType) : Attribute
{
    public Type SnapshotType { get; } = snapshotType;
    public Type ProjectionType { get; } = projectionType;
}
