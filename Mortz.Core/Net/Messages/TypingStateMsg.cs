namespace Mortz.Core.Net.Messages;

/// <summary>Authoritative typing indicator for one player; goes false when
/// that peer leaves.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TypingStateMsg(long PeerId, bool IsTyping);
