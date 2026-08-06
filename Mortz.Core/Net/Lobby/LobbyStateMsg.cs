namespace Mortz.Core.Net.Lobby;

/// <summary>Everyone in the pre-match lobby with their ready state and lobby
/// team, plus the pending swap offers.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbyStateMsg(
    LobbyMember[] Members,
    SwapOffer[] Offers);
