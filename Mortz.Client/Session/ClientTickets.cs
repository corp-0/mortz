namespace Mortz.Client.Session;

public enum ClientTicketState { WAITING, READY, FAILED }

public interface IClientTicket : IDisposable
{
    ClientTicketState State { get; }
    ulong AccountId { get; }
    ReadOnlyMemory<byte> Bytes { get; }
}

public interface IClientTicketProvider
{
    bool Available { get; }
    IClientTicket? Create(ulong recipient);
}

public class GuestTicketProvider : IClientTicketProvider
{
    public bool Available => false;
    public IClientTicket? Create(ulong recipient) => null;
}
