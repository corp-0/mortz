namespace Mortz.Protocol.Net.Sim;

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TerrainChunkMsg(
    int TransferId, short Index, short Count, byte[] Data, int MatchGeneration = 0);

/// <summary>ModifierWire data sent with each roster and whenever modifiers change.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PlayerModifiersMsg(int PeerId, byte[] Modifiers, int Revision = 0, int EffectiveTick = 0, int MatchGeneration = 0);
