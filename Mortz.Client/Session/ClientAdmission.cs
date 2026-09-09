using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;

namespace Mortz.Client.Session;

public enum ClientAdmissionStage { IDLE, OFFER, TICKET, ACCEPTANCE, ADMITTED, REJECTED }

public interface IClientAdmissionTransport
{
    void Hello(AdmissionHello hello);
    void Proof(SteamProof proof);
}

public class ClientAdmission(IClientTicketProvider tickets, IClientAdmissionTransport transport, Func<ulong> clock) : IDisposable
{
    private IClientTicket? _ticket;
    private ulong _deadline;
    private ulong _ticketDeadline;
    private ulong _knownRecipient;
    private bool _steam;
    private int _attempt;
    private AdmissionMode _mode;
    public ClientAdmissionStage Stage { get; private set; }
    public AdmissionMode? AcceptedMode => Stage == ClientAdmissionStage.ADMITTED ? _mode : null;
    public event Action<AdmissionMode>? Accepted;
    public event Action<AdmissionRejection>? Rejected;

    public void Begin(string name, int skin, ulong now, ulong knownRecipient = 0)
    {
        Reset();
        _knownRecipient = knownRecipient;
        _steam = tickets.Available;
        _deadline = AdmissionLimits.Deadline(now, AdmissionLimits.CLIENT_MS);
        Stage = ClientAdmissionStage.OFFER;
        transport.Hello(new AdmissionHello(NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, name, skin, _steam));
    }

    public void Offer(AdmissionOffer offer, ulong now)
    {
        if (Stage is ClientAdmissionStage.IDLE or ClientAdmissionStage.REJECTED)
        {
            return;
        }
        if (now >= _deadline)
        {
            Fail(AdmissionRejection.ADMISSION_TIMEOUT);
            return;
        }
        if (Stage != ClientAdmissionStage.OFFER || offer.Attempt != 1 ||
            !Enum.IsDefined(offer.Mode) || (offer.Mode == AdmissionMode.GUEST && offer.ServerAccountId != 0))
        {
            Fail(AdmissionRejection.INVALID_STATE);
            return;
        }
        _attempt = offer.Attempt;
        _mode = offer.Mode;
        if (_mode == AdmissionMode.GUEST)
        {
            Stage = ClientAdmissionStage.ACCEPTANCE;
            return;
        }
        if (!_steam || !tickets.Available || offer.ServerAccountId == 0)
        {
            Fail(AdmissionRejection.AUTHENTICATION_UNAVAILABLE);
            return;
        }
        if (_knownRecipient != 0 && offer.ServerAccountId != _knownRecipient)
        {
            Fail(AdmissionRejection.RECIPIENT_MISMATCH);
            return;
        }
        Stage = ClientAdmissionStage.TICKET;
        _ticketDeadline = AdmissionLimits.Deadline(now, AdmissionLimits.TICKET_MS);
        try { _ticket = tickets.Create(offer.ServerAccountId); }
        catch { _ticket = null; }
        if (_ticket == null)
        {
            Fail(AdmissionRejection.TICKET_FAILED);
        }
    }

    public void Accept(AdmissionAccepted accepted)
    {
        if (Stage is ClientAdmissionStage.IDLE or ClientAdmissionStage.REJECTED)
        {
            return;
        }
        if (clock() >= _deadline)
        {
            Fail(AdmissionRejection.ADMISSION_TIMEOUT);
            return;
        }
        if (Stage != ClientAdmissionStage.ACCEPTANCE || accepted.Attempt != _attempt || accepted.Mode != _mode)
        {
            Fail(AdmissionRejection.INVALID_STATE);
            return;
        }
        Stage = ClientAdmissionStage.ADMITTED;
        Accepted?.Invoke(_mode);
    }

    public void Reject(AdmissionRejected rejected)
    {
        if (Stage is ClientAdmissionStage.IDLE or ClientAdmissionStage.REJECTED)
        {
            return;
        }
        if ((_attempt != 0 && rejected.Attempt != _attempt) || rejected.Attempt is < 0 or > 1 ||
            !Enum.IsDefined(rejected.Reason))
        {
            Fail(AdmissionRejection.INVALID_STATE);
            return;
        }
        Fail(rejected.Reason);
    }

    public void Advance(ulong now)
    {
        if (Stage is ClientAdmissionStage.IDLE or ClientAdmissionStage.ADMITTED or ClientAdmissionStage.REJECTED)
        {
            return;
        }
        if (now >= _deadline)
        {
            Fail(AdmissionRejection.ADMISSION_TIMEOUT);
            return;
        }
        if (Stage != ClientAdmissionStage.TICKET)
        {
            return;
        }
        if (now >= _ticketDeadline)
        {
            Fail(AdmissionRejection.TICKET_TIMEOUT);
            return;
        }
        if (_ticket!.State == ClientTicketState.FAILED || !tickets.Available)
        {
            Fail(AdmissionRejection.TICKET_FAILED);
            return;
        }
        if (_ticket.State != ClientTicketState.READY)
        {
            return;
        }
        if (_ticket.AccountId == 0 || _ticket.Bytes.Length is 0 or > AdmissionLimits.TICKET_BYTES)
        {
            Fail(AdmissionRejection.TICKET_FAILED);
            return;
        }
        Stage = ClientAdmissionStage.ACCEPTANCE;
        transport.Proof(new SteamProof(_attempt, _ticket.AccountId, _ticket.Bytes.ToArray()));
    }

    private void Fail(AdmissionRejection reason)
    {
        Stage = ClientAdmissionStage.REJECTED;
        ReleaseTicket();
        Rejected?.Invoke(reason);
    }

    private void ReleaseTicket()
    {
        IClientTicket? ticket = _ticket;
        _ticket = null;
        ticket?.Dispose();
    }

    public void Reset()
    {
        Stage = ClientAdmissionStage.IDLE;
        _attempt = 0;
        ReleaseTicket();
    }

    public void Dispose() => Reset();
}
