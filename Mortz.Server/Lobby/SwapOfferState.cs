using Mortz.Server.Players;

namespace Mortz.Server.Lobby;

/// <summary>Lobby-lifetime cell: this player's one outgoing swap offer, null
/// while none is pending.</summary>
public sealed class SwapOfferState
{
    public Player? Target;
}
