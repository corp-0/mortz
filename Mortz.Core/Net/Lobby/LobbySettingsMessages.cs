namespace Mortz.Core.Net.Lobby;

/// <summary>
/// The server's complete lobby setup snapshot.
/// </summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbySettingsMsg(
    string MapId,
    string MapHash,
    string[] MapIds,
    string[] MapNames,
    string[] ModeIds,
    string[] ModeNames,
    string ModeId,
    byte[] Config);

/// <summary>Signed admin request to select a server map.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyMapUpdateMsg(
    string MapId,
    ulong Sequence,
    byte[] Tag);

/// <summary>Signed admin request to select a game mode and apply its rules.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyModeUpdateMsg(
    string ModeId,
    ulong Sequence,
    byte[] Tag);

/// <summary>Signed admin request to replace all lobby rules.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyRulesUpdateMsg(
    byte[] Config,
    ulong Sequence,
    byte[] Tag);
