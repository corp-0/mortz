using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

public static class LobbySettingsProtocol
{
    static LobbySettingsProtocol() => LobbySettingsMsg.Received += OnReceived;

    public static event Action<LobbySettings>? Received;
    public static event Action<LobbySettingsRejectReason>? Rejected;

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

    private static void OnReceived(LobbySettingsMsg message)
    {
        if (!TryCatalog(message.MapIds, message.MapNames, NetConfig.MAX_LOBBY_MAPS,
                out LobbyCatalog maps))
        {
            Rejected?.Invoke(LobbySettingsRejectReason.MAP_CATALOG);
            return;
        }
        if (!TryCatalog(message.ModeIds, message.ModeNames, NetConfig.MAX_LOBBY_MODES,
                out LobbyCatalog modes))
        {
            Rejected?.Invoke(LobbySettingsRejectReason.MODE_CATALOG);
            return;
        }

        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            Rejected?.Invoke(LobbySettingsRejectReason.CONFIG);
            return;
        }

        Received?.Invoke(new LobbySettings(
            new LobbySelection(message.MapId, message.MapHash, maps, modes,
                message.ModeId.Length == 0 ? null : message.ModeId),
            config));
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
