using Mortz.Core.Chat;

namespace Mortz.Core.Net.Messages;

public enum ChatLineKind : byte
{
    PLAYER,
    SYSTEM,
    ROLL,
}

/// <summary>
/// An authoritative chat/feed line published by the server. Rich text contains
/// BBCode assembled from escaped text and code-owned styles.
/// </summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ChatLineMsg(
    ChatLineKind Kind,
    long SenderId,
    string SenderName,
    string Text,
    ChatTextFormat TextFormat);
