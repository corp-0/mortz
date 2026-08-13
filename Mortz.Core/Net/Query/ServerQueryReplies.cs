namespace Mortz.Core.Net.Query;

public readonly record struct ServerProbeReply(
    ServerEndpoint Endpoint, ServerInfo Info, int PingMs);

/// <summary>
/// Server information for the server browser.
/// </summary>
public readonly record struct ServerQueryReply(uint Nonce, ServerInfo Info);
