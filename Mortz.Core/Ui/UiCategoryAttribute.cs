namespace Mortz.Core.Ui;

/// <summary>Opens a category at this property; the [UiProperty] members below
/// stay in it until the next marker.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiCategoryAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}
