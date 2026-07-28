namespace Mortz.Core.Net.Query;

public readonly record struct ServerQueryReply(uint Nonce, ServerInfo Info);
