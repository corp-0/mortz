using Mortz.Core.Net;
using Mortz.Core.Net.Stats;

namespace Mortz.Server;

/// <summary>Raw server wire transport. Server features never receive this;
/// ReadyLink decorates it with readiness guarantees first.</summary>
public interface IServerTransport
{
    void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg>;

    void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>;

    void Disconnect(int peerId, string reason);

    int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor);

    int SendSnapshot(int peerId, byte[] data, int ack);

    PeerPing[] PeerPings();

    WireStats PopWireStats();
}
