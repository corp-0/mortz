namespace Mortz.Client.Servers;

/// <summary>A favorite as persisted in user://settings.json.</summary>
public sealed record FavoriteServer(string Address, int Port, string Label = "");
