namespace Mortz.Client.Servers;

/// <summary>A favorite persisted in the local player profile.</summary>
public sealed record FavoriteServer(string Address, int Port, string Label = "");
