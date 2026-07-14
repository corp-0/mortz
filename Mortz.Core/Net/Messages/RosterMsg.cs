namespace Mortz.Core.Net.Messages;

/// <summary>Name list of everyone in the match, for nameplates. Sent on match
/// start and on every in-game join/leave; the lobby uses LobbyStateMsg instead.
/// Teams is reserved for upcoming team-colored presentation and roster UI.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct RosterMsg(
    long[] PeerIds, string[] Names, byte[] Skins, byte[] Teams, byte[] Slots);
