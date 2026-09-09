#if MORTZ_STEAM
using Godot;
using Mortz.Client.Session;
using Mortz.Protocol.Net.Admission;
using SteamApi = GDExtension.Wrappers.Steam;

namespace Mortz.Platform.Steam;

public class SteamClientTickets(SteamApi api, Func<bool> available) : IClientTicketProvider, IDisposable
{
    private readonly Dictionary<long, Ticket> _tickets = [];
    private bool _disposed;
    public bool Available => !_disposed && available();

    public void Ready(long handle, long result)
    {
        if (_tickets.TryGetValue(handle, out Ticket? ticket))
        {
            ticket.Complete(result == 1);
        }
    }

    public IClientTicket? Create(ulong recipient)
    {
        if (!Available || recipient == 0)
        {
            return null;
        }
        using Godot.Collections.Dictionary result = api.GetAuthSessionTicket(unchecked((long)recipient));
        long handle = result.TryGetValue("id", out Variant id) ? id.AsInt64() : 0;
        if (handle == 0)
        {
            return null;
        }
        try
        {
            byte[] buffer = result["buffer"].AsByteArray();
            int length = result["size"].AsInt32();
            if (length is <= 0 or > AdmissionLimits.TICKET_BYTES || length > buffer.Length)
            {
                api.CancelAuthTicket(handle);
                return null;
            }
            Ticket ticket = new(api.GetSteamId(), buffer.AsSpan(0, length).ToArray(), () => Cancel(handle));
            _tickets.Add(handle, ticket);
            return ticket;
        }
        catch
        {
            api.CancelAuthTicket(handle);
            return null;
        }
    }

    private void Cancel(long handle)
    {
        if (_tickets.Remove(handle))
        {
            api.CancelAuthTicket(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (Ticket ticket in _tickets.Values.ToArray())
        {
            ticket.Dispose();
        }
    }

    private class Ticket(ulong accountId, byte[] bytes, Action cancel) : IClientTicket
    {
        private bool _disposed;
        public ClientTicketState State { get; private set; }
        public ulong AccountId => accountId;
        public ReadOnlyMemory<byte> Bytes => bytes;
        public void Complete(bool success)
        {
            if (!_disposed && State == ClientTicketState.WAITING)
            {
                State = success ? ClientTicketState.READY : ClientTicketState.FAILED;
            }
        }
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            State = ClientTicketState.FAILED;
            Array.Clear(bytes);
            cancel();
        }
    }
}
#endif
