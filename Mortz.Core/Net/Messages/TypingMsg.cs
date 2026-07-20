namespace Mortz.Core.Net.Messages;

/// <summary>A player opened or closed their chat input. The server dedupes
/// and rebroadcasts as <see cref="TypingStateMsg"/>.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TypingMsg(bool IsTyping);
