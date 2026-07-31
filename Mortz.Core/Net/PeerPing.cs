namespace Mortz.Core.Net;

[NetRow]
public readonly partial record struct PeerPing(int PeerId, int PingMs);
