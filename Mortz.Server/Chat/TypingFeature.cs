using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Server.Features;
using Mortz.Server.Players;

namespace Mortz.Server.Chat;

/// <summary>Typing indicators. A leaver's state clears because their cell is
/// still readable during the leave fan-out.</summary>
public sealed class TypingFeature(ServerStateKeys keys, IServerLink link) : IHandle<Player, TypingMsg>, IObservePlayers
{
    private readonly ServerStateKey<TypingState> _typing = keys.Claim<TypingState>();

    public void Handle(Player sender, in TypingMsg message)
    {
        if (sender.State(_typing).Set(message.IsTyping))
            link.Broadcast(new TypingStateMsg(sender.PeerId, message.IsTyping));
    }

    public void PlayerJoined(Player player)
    {
    }

    public void PlayerLeft(Player player)
    {
        if (player.State(_typing).Set(false))
            link.Broadcast(new TypingStateMsg(player.PeerId, false));
    }
}
