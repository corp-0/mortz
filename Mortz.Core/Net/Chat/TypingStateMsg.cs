namespace Mortz.Core.Net.Chat;

/// <summary>Authoritative typing indicator for one player; goes false when
/// that peer leaves.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct TypingStateMsg(int PeerId, bool IsTyping);
