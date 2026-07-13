namespace Mortz.Core.Net.Messages;

/// <summary>X/Y = body center at the moment of death, for gib/blood effects.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct DeathMsg(long PeerId, int X, int Y);
