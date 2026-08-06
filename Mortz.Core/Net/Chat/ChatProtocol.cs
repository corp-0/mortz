using System.Diagnostics.CodeAnalysis;
using Mortz.Core.Chat;

namespace Mortz.Core.Net.Chat;

public static class ChatProtocol
{
    public static ChatMsg Encode(ChatLine.Remote line) => line switch
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

    public static bool TryDecode(
        ChatMsg message,
        [NotNullWhen(true)] out ChatLine.Remote? line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(message.Text))
            return false;

        switch (Kind: message.MsgKind, message.TextFormat)
        {
            case (ChatMsgKind.PLAYER, ChatTextFormat.MARKDOWN)
                when TrySender(message, out string? playerName):
                line = new ChatLine.Player(message.SenderId, playerName, message.Text);
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
                when TrySender(message, out string? rollerName) &&
                     DiceRoll.TryParse(message.Text, out int value):
                line = new ChatLine.Roll(message.SenderId, rollerName, value);
                return true;
            default:
                return false;
        }
    }

    private static bool TrySender(ChatMsg message, [NotNullWhen(true)] out string? name)
    {
        name = message.SenderName;
        return message.SenderId > 0;
    }

    private static bool ValidSystem(ChatMsg message) =>
        message is { SenderId: 0, SenderName: "Server" };
}
