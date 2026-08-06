using Mortz.Core.Net;

namespace Mortz.Tests.Core.Net;

/// <summary>Records what the client router delivers for one message, in
/// arrival order. The senderless twin of Probe.</summary>
public sealed class ClientProbe<TMsg> : IHandle<TMsg> where TMsg : struct
{
    public List<TMsg> Messages { get; } = [];

    public void Handle(in TMsg message) => Messages.Add(message);
}
