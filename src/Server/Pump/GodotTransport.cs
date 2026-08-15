using Mortz.Core.Net;
using Mortz.Core.Net.Stats;
using Mortz.Net;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Server.Pump;

/// <summary>Raw server transport over the Godot ENet autoload.</summary>
public sealed class GodotTransport(NetworkManager network) : IServerTransport
{
    private static readonly ILogger _log = MortzLog.For("server");

    public void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg> =>
        network.SendEnvelope(TMsg.MsgId, TMsg.Serialize(in message), peerId, TMsg.MsgChannel);

    public void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg> =>
        network.SendEnvelope(TMsg.MsgId, TMsg.Serialize(in message),
            NetConfig.BROADCAST_PEER_ID, TMsg.MsgChannel);

    public void Disconnect(int peerId, string reason)
    {
        _log.Information("disconnecting peer {PeerId}: {Reason}", peerId, reason);
        network.Kick(peerId);
    }

    public int BroadcastSnapshot(Func<int, byte[]> dataFor, Func<int, int> ackFor) =>
        network.BroadcastSnapshot(dataFor, ackFor);

    public int SendSnapshot(int peerId, byte[] data, int ack) =>
        network.SendSnapshot(peerId, data, ack);

    public PeerPing[] PeerPings() => network.PeerPingsMs();

    public WireStats PopWireStats() => network.PopWireStats();
}
