namespace Mortz.Core.Net.Messages;

/// <summary>Lobby ready toggle; the match starts once everyone is ready.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct SetReadyMsg(bool Ready);
