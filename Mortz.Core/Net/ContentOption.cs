namespace Mortz.Core.Net;

public readonly record struct ContentOption
{
    public ContentOption(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Content option id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Content option name cannot be empty.", nameof(name));
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
}
