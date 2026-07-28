namespace Mortz.Client.Setup;

/// <summary>One selectable catalog entry (map or mode) from the server.</summary>
public readonly record struct ContentOption(string Id, string Name);
