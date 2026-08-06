using Mortz.Core.Net;

namespace Mortz.Tests.Net;

/// <summary>Puts a server-to-client message on the transport (a loopback into
/// client dispatch in tests). The generator only emits Broadcast/SendTo for
/// client-to-server messages now, so a test playing the server needs this to
/// move the bytes.</summary>
public static class ServerWire
{
    public static void Broadcast<TMsg>(this TMsg message)
        where TMsg : struct, INetMessage<TMsg> =>
        NetTransport.Send(
            TMsg.MsgId, TMsg.Serialize(in message), NetTransport.BROADCAST, TMsg.MsgChannel);

    public static void SendTo<TMsg>(this TMsg message, int peerId)
        where TMsg : struct, INetMessage<TMsg> =>
        NetTransport.Send(TMsg.MsgId, TMsg.Serialize(in message), peerId, TMsg.MsgChannel);
}
