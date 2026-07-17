namespace Mortz.Core.Net.Messages;

/// <summary>Parallel arrays: match wins per player for the current server
/// session. Sent to a joining peer and broadcast whenever a match is won.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct SessionWinsMsg(long[] PeerIds, int[] Wins);
