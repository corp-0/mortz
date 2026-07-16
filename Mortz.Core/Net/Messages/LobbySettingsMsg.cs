namespace Mortz.Core.Net.Messages;

/// <summary>Canonical server-owned setup shown by every lobby client.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbySettingsMsg(
    string MapId,
    string MapHash,
    string[] MapIds,
    string[] MapNames,
    byte[] Config);
