namespace Mortz.Core.Net.Messages;

/// <summary>Requests the current canonical setup after entering the lobby.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbySettingsRequestMsg();
