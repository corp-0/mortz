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

/// <summary>Markdown chat text; slash commands use separate messages.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct ChatSendMsg(string Text);

[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct RollRequestMsg;

[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TypingMsg(bool IsTyping);

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TypingStateMsg(int PeerId, bool IsTyping);
