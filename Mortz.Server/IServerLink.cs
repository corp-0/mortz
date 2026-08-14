using Mortz.Core.Net;
using Mortz.Core.Net.Stats;

namespace Mortz.Server;

/// <summary>The readiness-safe link exposed to server services. Every outgoing
/// message passes through this boundary.</summary>
public interface IServerLink
{
    void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg>;

    void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>;

    void Disconnect(int peerId, string reason);

    /// <summary>Hot path stays bespoke: per-peer snapshot bytes + ack. Returns app payload bytes.</summary>
    int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor);

    int SendSnapshot(int peerId, byte[] data, int ack);

    PeerPing[] PeerPings();

    WireStats PopWireStats();
}
