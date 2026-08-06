namespace Mortz.Core.Net.Stats;

[NetRow]
public readonly partial record struct PeerPing(int PeerId, int PingMs);
