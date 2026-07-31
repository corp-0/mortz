namespace Mortz.Core.Net.Messages;

/// <summary>Everyone in the match with their skin, team and net slot, for
/// nameplates and snapshot slot resolution. Sent on match start and on every
/// in-game join/leave; the lobby uses LobbyStateMsg instead.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct RosterMsg(
    int[] PeerIds, string[] Names, byte[] Skins, byte[] Teams, byte[] Slots);
