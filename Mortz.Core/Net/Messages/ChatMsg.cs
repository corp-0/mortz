namespace Mortz.Core.Net.Messages;

internal enum ChatMsgKind : byte
{
    PLAYER,
    SYSTEM,
    ROLL,
}

internal enum ChatTextFormat : byte
{
    PLAIN,
    MARKDOWN,
    RICH_TEXT,
}

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
internal readonly partial record struct ChatMsg(
    ChatMsgKind MsgKind,
    int SenderId,
    string SenderName,
    string Text,
    ChatTextFormat TextFormat);
