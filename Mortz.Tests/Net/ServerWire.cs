using Mortz.Core.Net;

namespace Mortz.Tests.Net;

/// <summary>Dispatches a server-to-client message into a client router.</summary>
public static class ServerWire
{
    public static byte[] Payload<TMsg>(this TMsg message)
        where TMsg : struct, INetMessage<TMsg> =>
        TMsg.Serialize(in message);

    public static void Broadcast<TMsg>(this TMsg message, NetRouter router)
        where TMsg : struct, INetMessage<TMsg> =>
        router.Dispatch(TMsg.MsgId, TMsg.Serialize(in message));

    public static void SendTo<TMsg>(this TMsg message, NetRouter router, int peerId)
        where TMsg : struct, INetMessage<TMsg> =>
        router.Dispatch(TMsg.MsgId, TMsg.Serialize(in message));
}
