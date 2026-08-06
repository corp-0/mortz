namespace Mortz.Server.Settings;

public readonly record struct LobbySettingDelta(string Name, string Before, string After);
