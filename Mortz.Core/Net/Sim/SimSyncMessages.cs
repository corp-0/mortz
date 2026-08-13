namespace Mortz.Core.Net.Sim;

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TerrainChunkMsg(
    int TransferId, short Index, short Count, byte[] Data);

/// <summary>ModifierWire data sent with each roster and whenever modifiers change.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PlayerModifiersMsg(int PeerId, byte[] Modifiers);
