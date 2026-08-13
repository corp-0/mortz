using Mortz.Core.Net;

namespace Mortz.Tests.Core.Net;

/// <summary>One message a router delivered, with the sender it carried.</summary>
public readonly record struct Delivery<TMsg>(int Sender, TMsg Message) where TMsg : struct;

/// <summary>Records what the router delivers for one message, in arrival order.</summary>
public sealed class Probe<TMsg> : IHandle<int, TMsg> where TMsg : struct
{
    public List<Delivery<TMsg>> Deliveries { get; } = [];

    public void Handle(int sender, in TMsg message) => Deliveries.Add(new(sender, message));
}
