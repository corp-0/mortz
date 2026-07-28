using JetBrains.Annotations;

namespace Mortz.Core.Ui;

/// <summary>Shows a UI property only while the named bool property is true.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiVisibleWhenAttribute(string predicate) : Attribute
{
    // UiMetadataGenerator reads this from metadata, never through the getter.
    [UsedImplicitly] public string Predicate { get; } = predicate;
}
