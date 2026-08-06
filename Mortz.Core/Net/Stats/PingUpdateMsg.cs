namespace Mortz.Core.Net.Stats;

/// <summary>Server-measured round-trip time in ms for every connected player.
/// Broadcast about once a second in lobby and in match; a lost update is
/// superseded by the next one, hence unreliable.</summary>
[NetMessage(NetChannel.UNRELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PingUpdateMsg(PeerPing[] Pings);
