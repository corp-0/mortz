namespace Mortz.Core.Net.Stats;

[NetRow]
public readonly partial record struct PeerPing(int PeerId, int PingMs);

/// <summary>Server-measured RTT sent about once a second; lost updates are replaced by the next.</summary>
[NetMessage(NetChannel.UNRELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PingUpdateMsg(PeerPing[] Pings);
