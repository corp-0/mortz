namespace Mortz.Protocol.Net.Query;

public readonly record struct ServerProbeReply(
    ServerEndpoint Endpoint, ServerInfo Info, int PingMs, string? ResolvedAddress = null);
