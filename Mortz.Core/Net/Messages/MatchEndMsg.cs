namespace Mortz.Core.Net.Messages;

/// <summary>The match is decided. WinnerId is a team id when ByTeam, a peer
/// id otherwise.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchEndMsg(bool ByTeam, long WinnerId);
