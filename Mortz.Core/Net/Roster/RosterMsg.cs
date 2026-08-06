namespace Mortz.Core.Net.Roster;

/// <summary>Everyone in the match: skin, team, net slot. Sent on match start
/// and on every in-game join/leave; the lobby uses LobbyStateMsg instead.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct RosterMsg(RosterEntry[] Entries);
