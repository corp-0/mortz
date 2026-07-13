namespace Mortz.Core.Net.Messages;

/// <summary>Terrain carve event. OwnerId/SpawnSeq identify the shell so the
/// owner's client can confirm its predicted carve; 0/-1 for carves nobody
/// predicted (debug).</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct CarveMsg(int X, int Y, int Radius, int OwnerId, int SpawnSeq);
