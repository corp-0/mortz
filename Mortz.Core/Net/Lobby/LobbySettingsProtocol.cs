using System.Diagnostics.CodeAnalysis;
using Mortz.Core.Match.Configuration;

namespace Mortz.Core.Net.Lobby;

public static class LobbySettingsProtocol
{
    public static LobbySettingsMsg Encode(LobbySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LobbySelection selection = settings.Selection;
        return new LobbySettingsMsg(
            selection.MapId,
            selection.MapHash,
            [.. selection.Maps.Options],
            [.. selection.Modes.Options],
            selection.ModeId ?? "",
            settings.Config.ToBytes());
    }

    /// <summary>Reason is meaningful only on false.</summary>
    public static bool TryDecode(
        LobbySettingsMsg message,
        [NotNullWhen(true)] out LobbySettings? settings,
        out LobbySettingsRejectReason reason)
    {
        settings = null;
        if (!TryCatalog(message.MapOptions, NetConfig.MAX_LOBBY_MAPS,
                out LobbyCatalog maps))
        {
            reason = LobbySettingsRejectReason.MAP_CATALOG;
            return false;
        }
        if (!TryCatalog(message.ModeOptions, NetConfig.MAX_LOBBY_MODES,
                out LobbyCatalog modes))
        {
            reason = LobbySettingsRejectReason.MODE_CATALOG;
            return false;
        }

        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            reason = LobbySettingsRejectReason.CONFIG;
            return false;
        }

        settings = new LobbySettings(
            new LobbySelection(message.MapId, message.MapHash, maps, modes,
                message.ModeId.Length == 0 ? null : message.ModeId),
            config);
        reason = default;
        return true;
    }

    private static bool TryCatalog(ContentOption[] rows, int cap,
        out LobbyCatalog options)
    {
        options = LobbyCatalog.EMPTY;
        if (rows.Length > cap)
            return false;
        foreach (ContentOption row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Name))
                return false;
        }
        options = new LobbyCatalog(rows);
        return true;
    }
}
