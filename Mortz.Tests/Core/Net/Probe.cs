using Mortz.Core.Net;

namespace Mortz.Tests.Core.Net;

/// <summary>Records what the router delivers for one message, in arrival order.</summary>
public sealed class Probe<TMsg> : IHandle<int, TMsg> where TMsg : struct
{
    public List<Delivery<TMsg>> Deliveries { get; } = [];

    public void Handle(int sender, in TMsg message) => Deliveries.Add(new(sender, message));
}
