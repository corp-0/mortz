using System.Diagnostics.CodeAnalysis;
using Mortz.Core.Chat;
using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

public static class ChatProtocol
{
    static ChatProtocol() => ChatMsg.Received += OnReceived;

    public static event Action<ChatLine.Remote>? Received;

    public static void Broadcast(ChatLine.Remote line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Encode(line).Broadcast();
    }

    public static void SendTo(long peerId, ChatLine.Remote line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Encode(line).SendTo(peerId);
    }

    private static ChatMsg Encode(ChatLine.Remote line) => line switch
    {
        ChatLine.Player player => new ChatMsg(
            ChatMsgKind.PLAYER,
            player.SenderId,
            player.SenderName,
            player.Text,
            ChatTextFormat.MARKDOWN),
        ChatLine.System system => new ChatMsg(
            ChatMsgKind.SYSTEM,
            0,
            "Server",
            system.Text,
            ChatTextFormat.RICH_TEXT),
        ChatLine.Roll roll => new ChatMsg(
            ChatMsgKind.ROLL,
            roll.SenderId,
            roll.SenderName,
            roll.Value.ToString(),
            ChatTextFormat.PLAIN),
        _ => throw new ArgumentOutOfRangeException(nameof(line)),
    };

    private static void OnReceived(ChatMsg message)
    {
        if (TryDecode(message, out ChatLine.Remote? line))
            Received?.Invoke(line);
    }

    private static bool TryDecode(
        ChatMsg message,
        [NotNullWhen(true)] out ChatLine.Remote? line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(message.Text))
            return false;

        switch (Kind: message.MsgKind, message.TextFormat)
        {
            case (ChatMsgKind.PLAYER, ChatTextFormat.MARKDOWN)
                when ValidPeer(message.SenderId, message.SenderName):
                line = new ChatLine.Player(
                    message.SenderId,
                    message.SenderName,
                    message.Text);
                return true;
            case (ChatMsgKind.SYSTEM, ChatTextFormat.PLAIN)
                when ValidSystem(message):
                line = new ChatLine.System(message.Text);
                return true;
            case (ChatMsgKind.SYSTEM, ChatTextFormat.RICH_TEXT)
                when ValidSystem(message):
                line = ChatLine.System.FromTrustedBbCode(message.Text);
                return true;
            case (ChatMsgKind.ROLL, ChatTextFormat.PLAIN)
                when ValidPeer(message.SenderId, message.SenderName) &&
                     DiceRoll.TryParse(message.Text, out int value):
                line = new ChatLine.Roll(message.SenderId, message.SenderName, value);
                return true;
            default:
                return false;
        }
    }

    private static bool ValidPeer(long senderId, string senderName) =>
        senderId > 0 && !string.IsNullOrWhiteSpace(senderName);

    private static bool ValidSystem(ChatMsg message) =>
        message is { SenderId: 0, SenderName: "Server" };
}
