using System.Diagnostics.CodeAnalysis;
using Mortz.Core.Match;
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
            selection.Maps.Options.Select(option => option.Id).ToArray(),
            selection.Maps.Options.Select(option => option.Name).ToArray(),
            selection.Modes.Options.Select(option => option.Id).ToArray(),
            selection.Modes.Options.Select(option => option.Name).ToArray(),
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
        if (!TryCatalog(message.MapIds, message.MapNames, NetConfig.MAX_LOBBY_MAPS,
                out LobbyCatalog maps))
        {
            reason = LobbySettingsRejectReason.MAP_CATALOG;
            return false;
        }
        if (!TryCatalog(message.ModeIds, message.ModeNames, NetConfig.MAX_LOBBY_MODES,
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

    private static bool TryCatalog(string[] ids, string[] names, int cap,
        out LobbyCatalog options)
    {
        options = LobbyCatalog.EMPTY;
        if (ids.Length != names.Length || ids.Length > cap)
            return false;
        ContentOption[] parsed = new ContentOption[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(ids[i]) || string.IsNullOrWhiteSpace(names[i]))
                return false;
            parsed[i] = new ContentOption(ids[i], names[i]);
        }
        options = new LobbyCatalog(parsed);
        return true;
    }
}
