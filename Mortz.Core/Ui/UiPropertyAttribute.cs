namespace Mortz.Core.Ui;

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiPropertyAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}
