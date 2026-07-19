namespace Mortz.Core.Net.Messages;

/// <summary>One player's persistent modifier list (ModifierWire blob),
/// broadcast alongside every roster: match start and every in-game
/// join/leave, plus whenever a modifier changes. Clients resolve it through
/// the same StatsPipeline as the server, so views and prediction stay
/// bit-identical with the sim.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PlayerModifiersMsg(long PeerId, byte[] Modifiers);
