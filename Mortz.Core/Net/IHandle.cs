namespace Mortz.Core.Net;

/// <summary>Receives one decoded message from an already-resolved sender.</summary>
public interface IHandle<TSender, TMsg>
    where TMsg : struct
{
    void Handle(TSender sender, in TMsg message);
}

/// <summary>Client side: no sender to resolve, there's only the server.</summary>
public interface IHandle<TMsg>
    where TMsg : struct
{
    void Handle(in TMsg message);
}
