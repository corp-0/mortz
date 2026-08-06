namespace Mortz.Core.Net.Chat;

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ChatMsg(
    ChatMsgKind MsgKind,
    int SenderId,
    string SenderName,
    string Text,
    ChatTextFormat TextFormat);

public enum ChatMsgKind : byte
{
    PLAYER,
    SYSTEM,
    ROLL,
}

public enum ChatTextFormat : byte
{
    PLAIN,
    MARKDOWN,
    RICH_TEXT,
}
