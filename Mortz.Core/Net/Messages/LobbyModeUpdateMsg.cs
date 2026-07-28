namespace Mortz.Core.Net.Messages;

/// <summary>Signed admin request to apply a server-catalog game mode's rules.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyModeUpdateMsg(
    string ModeId,
    ulong Sequence,
    byte[] Tag);
