namespace Mortz.Core.Net.Messages;

/// <summary>X/Y = body center at the moment of death, for gib/blood effects.
/// Owned = a parried shell killed its own shooter; feeds the OWNED sfx.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct DeathMsg(long PeerId, int X, int Y, bool Owned);
