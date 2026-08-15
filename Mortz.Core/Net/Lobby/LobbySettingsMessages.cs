using System.Text;
using Mortz.Core.Admin;

namespace Mortz.Core.Net.Lobby;

/// <summary>
/// The server's complete lobby setup snapshot.
/// </summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbySettingsMsg(
    string MapId,
    string MapHash,
    ContentOption[] MapOptions,
    ContentOption[] ModeOptions,
    string ModeId,
    byte[] Config);

/// <summary>Signed admin request to select a server map.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyMapUpdateMsg(
    string MapId,
    ulong Sequence,
    byte[] Tag);

public static class SetLobbyMapAction
{
    public const byte ACTION = AdminAction.SET_LOBBY_MAP;

    public static byte[] SignablePayload(string mapId) => Encoding.UTF8.GetBytes(mapId);
}

/// <summary>Signed admin request to select a game mode and apply its rules.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyModeUpdateMsg(
    string ModeId,
    ulong Sequence,
    byte[] Tag);

public static class SetLobbyModeAction
{
    public const byte ACTION = AdminAction.SET_LOBBY_MODE;

    public static byte[] SignablePayload(string modeId) => Encoding.UTF8.GetBytes(modeId);
}

/// <summary>Signed admin request to replace all lobby rules.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyRulesUpdateMsg(
    byte[] Config,
    ulong Sequence,
    byte[] Tag);

public static class ReplaceLobbyRulesAction
{
    public const byte ACTION = AdminAction.SET_LOBBY_RULES;

    public static byte[] SignablePayload(ReadOnlySpan<byte> config) => config.ToArray();
}
