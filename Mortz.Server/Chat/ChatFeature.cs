using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Core.Text;
using Mortz.Server.Features;
using Mortz.Server.Players;
using Mortz.Server.Settings;

namespace Mortz.Server.Chat;

/// <summary>Player chat, dice rolls and the system lines around them.</summary>
public sealed class ChatFeature(ServerStateKeys keys, IServerLink link, ServerClock clock, Random dice)
    :
        IHandle<Player, ChatSendMsg>,
        IHandle<Player, RollRequestMsg>,
        IObservePlayers
{
    private readonly ServerStateKey<ChatBudget> _budget = keys.Claim<ChatBudget>();

    public void Handle(Player sender, in ChatSendMsg message)
    {
        if (sender.State(_budget).TryAccept(clock.Ms, message.Text, out string text,
                out ChatRejectReason reason))
        {
            Broadcast(new ChatLine.Player(sender.PeerId, sender.Name, text));
            return;
        }

        // No reply to rate-limited senders, otherwise spam amplifies traffic.
        if (reason == ChatRejectReason.RATE_LIMITED)
            return;
        string status = reason switch
        {
            ChatRejectReason.COMMAND => "Unknown or invalid command.",
            ChatRejectReason.TOO_LONG =>
                $"Messages are limited to {NetConfig.MAX_CHAT_BYTES} UTF-8 bytes.",
            _ => "Message is empty.",
        };
        SendTo(sender.PeerId, new ChatLine.System(status));
    }

    /// <summary>Silent when rate limited, same as chat: replying would amplify spam.</summary>
    public void Handle(Player sender, in RollRequestMsg message)
    {
        if (!sender.State(_budget).TryAcceptRoll(clock.Ms))
            return;
        int value = dice.Next(DiceRoll.MIN, DiceRoll.MAX + 1);
        Broadcast(new ChatLine.Roll(sender.PeerId, sender.Name, value));
    }

    public void PlayerJoined(Player player)
    {
        RichText joined = new RichText()
            .Add(player.Name, new Style().Bold())
            .Add(" joined the game.");
        Broadcast(new ChatLine.System(joined));
    }

    public void PlayerLeft(Player player)
    {
        RichText left = new RichText()
            .Add(player.Name, new Style().Bold())
            .Add(" left the game.");
        Broadcast(new ChatLine.System(left));
    }

    public void AnnounceSettings(string adminName, LobbySettingDelta[] deltas)
    {
        foreach (LobbySettingDelta delta in deltas)
        {
            RichText text = new RichText()
                .Add(adminName, new Style().Color(RichTextColor.ACID))
                .Add(" changed ")
                .Add(delta.Name, new Style().Bold())
                .Add($" {delta.Before}")
                .Add(" > ")
                .Add(delta.After);

            Broadcast(new ChatLine.System(text));
        }
    }

    public void Broadcast(ChatLine.Remote line) => link.Broadcast(ChatProtocol.Encode(line));

    public void SendTo(int peerId, ChatLine.Remote line) =>
        link.Send(peerId, ChatProtocol.Encode(line));
}
