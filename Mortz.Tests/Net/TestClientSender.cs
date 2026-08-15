using Mortz.Core.Net;

namespace Mortz.Tests.Net;

public sealed class TestClientSender(Action<ushort, byte[], NetChannel>? sink = null) : IClientSender
{
    public List<(ushort Id, byte[] Payload, NetChannel Channel)> Sent { get; } = [];

    public void Send<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        byte[] payload = TMsg.Serialize(in message);
        Sent.Add((TMsg.MsgId, payload, TMsg.MsgChannel));
        sink?.Invoke(TMsg.MsgId, payload, TMsg.MsgChannel);
    }
}
