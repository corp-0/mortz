namespace Mortz.Core.Net.Messages;

/// <summary>One bounded reliable piece of late-join terrain state.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TerrainChunkMsg(
    int TransferId, short Index, short Count, byte[] Data);
