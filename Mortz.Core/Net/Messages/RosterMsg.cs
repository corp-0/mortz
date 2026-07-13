namespace Mortz.Core.Net.Messages;

/// <summary>Name list of everyone in the match, for nameplates. Sent on match
/// start and on every in-game join/leave; the lobby uses LobbyStateMsg instead.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct RosterMsg(long[] PeerIds, string[] Names);
