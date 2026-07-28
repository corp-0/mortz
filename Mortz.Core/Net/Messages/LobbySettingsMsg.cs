namespace Mortz.Core.Net.Messages;

/// <summary>Canonical server-owned setup shown by every lobby client.
/// ModeId is derived from Config: the mode whose identity keys currently
/// match, or "" when none do.</summary>
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
