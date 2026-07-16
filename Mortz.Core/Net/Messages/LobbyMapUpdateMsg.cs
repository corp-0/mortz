namespace Mortz.Core.Net.Messages;

/// <summary>Signed admin request to select a server-catalog map.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyMapUpdateMsg(
    string MapId,
    ulong Sequence,
    byte[] Tag);
