namespace Mortz.Core.Net.Messages;

/// <summary>Parallel arrays: everyone in the pre-match lobby with their ready state.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbyStateMsg(long[] PeerIds, string[] Names, byte[] ReadyFlags);
