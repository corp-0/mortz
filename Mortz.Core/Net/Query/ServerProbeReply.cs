namespace Mortz.Core.Net.Query;

public readonly record struct ServerProbeReply(
    ServerEndpoint Endpoint, ServerInfo Info, int PingMs);
