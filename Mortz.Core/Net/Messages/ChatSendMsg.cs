namespace Mortz.Core.Net.Messages;

/// <summary>
/// A player-authored Markdown chat line. Slash commands use their own messages;
/// rendering into BBCode remains a receiving-client concern.
/// </summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct ChatSendMsg(string Text);
