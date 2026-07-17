namespace Mortz.Core.Net.Messages;

/// <summary>A player offers to trade teams with someone across the divide.
/// Repeating it cancels the offer; the target offering back executes the
/// swap. The server answers with the lobby broadcast either way.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TeamSwapRequestMsg(long TargetPeerId);
