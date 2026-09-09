using System.Globalization;
using Mortz.Protocol.Net.Names;

namespace Mortz.Protocol.Net.Query;

public static class ServerQueryMetadata
{
    public static Dictionary<string, string> ToRules(ServerInfo info) => new(StringComparer.Ordinal)
    {
        ["mortz_query"] = ServerQueryProtocol.VERSION.ToString(CultureInfo.InvariantCulture),
        ["mortz_app"] = info.AppId.ToString(CultureInfo.InvariantCulture),
        ["mortz_protocol"] = info.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
        ["mortz_schema"] = info.SchemaHash.ToString("x16", CultureInfo.InvariantCulture),
        ["mortz_mode"] = SafeName.Sanitize(info.Mode, ServerQueryProtocol.MAX_TEXT_LENGTH),
        ["mortz_lobby"] = info.InLobby ? "1" : "0",
        ["mortz_join"] = info.AllowJoinInProgress ? "1" : "0",
        ["mortz_players"] = info.Players.ToString(CultureInfo.InvariantCulture)
    };

    public static ServerInfo ApplyRules(ServerInfo info, IReadOnlyDictionary<string, string> rules)
    {
        if (!rules.TryGetValue("mortz_query", out string? format) || format != "1" ||
            !rules.TryGetValue("mortz_app", out string? appText) ||
            !uint.TryParse(appText, NumberStyles.None, CultureInfo.InvariantCulture, out uint app) ||
            !rules.TryGetValue("mortz_protocol", out string? protocolText) ||
            !int.TryParse(protocolText, NumberStyles.None, CultureInfo.InvariantCulture, out int protocol) ||
            !rules.TryGetValue("mortz_schema", out string? schemaText) || schemaText.Length != 16 ||
            !ulong.TryParse(schemaText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong schema) ||
            !rules.TryGetValue("mortz_mode", out string? mode) || string.IsNullOrWhiteSpace(mode) ||
            !rules.TryGetValue("mortz_lobby", out string? lobby) || lobby is not ("0" or "1") ||
            !rules.TryGetValue("mortz_join", out string? join) || join is not ("0" or "1") ||
            !rules.TryGetValue("mortz_players", out string? playersText) ||
            !int.TryParse(playersText, NumberStyles.None, CultureInfo.InvariantCulture, out int players) ||
            players > info.MaxPlayers)
            return info with { MetadataValid = false };
        string safeMode = SafeName.Sanitize(mode, ServerQueryProtocol.MAX_TEXT_LENGTH);
        if (safeMode.Length == 0)
            return info with { MetadataValid = false };
        return info with
        {
            AppId = app,
            ProtocolVersion = protocol,
            SchemaHash = schema,
            Mode = safeMode,
            InLobby = lobby == "1",
            AllowJoinInProgress = join == "1",
            Players = players,
            MetadataValid = true
        };
    }
}
