namespace Mortz.Client.Servers;

/// <summary>A favorite persisted in the local player profile.</summary>
public record FavoriteServer(string Address, int Port, string Label = "", int? QueryPort = null);
