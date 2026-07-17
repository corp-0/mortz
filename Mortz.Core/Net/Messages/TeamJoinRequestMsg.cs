namespace Mortz.Core.Net.Messages;

/// <summary>A player asks to move onto a lobby team with a free slot. The
/// server validates capacity and answers with the lobby broadcast.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TeamJoinRequestMsg(byte Team);
