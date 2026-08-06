namespace Mortz.Content;

/// <summary>Generates a TOML reader and writer for this type and its object graph.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class TomlModelAttribute : Attribute;

/// <summary>Overrides the snake_case TOML name inferred from a constructor parameter.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TomlNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Leaves a writable or derived property out of TOML.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TomlIgnoreAttribute : Attribute;

/// <summary>Uses a discriminator field to select a concrete model type.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TomlUnionAttribute(string discriminator = "type") : Attribute
{
    public string Discriminator { get; } = discriminator;
}

/// <summary>Maps a stable TOML discriminator value to a concrete model type.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TomlCaseAttribute(string name, Type type) : Attribute
{
    public string Name { get; } = name;
    public Type Type { get; } = type;
}
