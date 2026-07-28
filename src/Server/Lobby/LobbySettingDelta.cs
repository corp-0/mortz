namespace Mortz.Server.Lobby;

public enum LobbySettingsChangeKind : byte
{
    RULES,
    MODE,
    MAP
}

public readonly record struct LobbySettingDelta(string Name, string Before, string After);

public readonly record struct LobbySettingsChange(
    long AdminId,
    LobbySettingsChangeKind Kind,
    IReadOnlyList<LobbySettingDelta> Deltas
)
{
    public bool AffectsConfig =>
        Kind is LobbySettingsChangeKind.RULES
            or LobbySettingsChangeKind.MODE;
}


