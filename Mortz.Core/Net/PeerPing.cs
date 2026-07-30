namespace Mortz.Core.Net;

public readonly record struct PeerPing(long PeerId, int PingMs);
