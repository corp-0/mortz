namespace Mortz.Core.Net.Messages;

/// <summary>Canonical server-owned setup shown by every lobby client.
/// ModeId is derived from Config: the mode whose preset the rules currently
/// match, or "" when they match none.</summary>
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
