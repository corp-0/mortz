namespace Mortz.Core.Net;

/// <summary>The client-side destination for typed messages sent to the server.</summary>
public interface IClientSender
{
    void Send<TMsg>(in TMsg message)
        where TMsg : struct, INetMessage<TMsg>;
}
