using Mortz.Client.Session;
using Mortz.Protocol.Net.Admission;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class ClientAdmissionTests
{
    [Fact]
    public void SteamWaitsForMatchingReadyTicketThenAcceptanceAndKeepsTicketUntilDisconnect()
    {
        using Fixture f = new();
        f.Begin();
        Assert.True(Assert.Single(f.Hellos).Steam);
        f.Offer();
        f.Admission.Advance(1);
        Assert.Empty(f.Proofs);
        Assert.Empty(f.Accepted);
        f.Provider.Ticket.State = ClientTicketState.READY;
        f.Admission.Advance(2);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(f.Proofs).Ticket);
        Assert.Equal(99UL, f.Provider.Recipient);
        Assert.Equal(0, f.Provider.Ticket.Cancels);
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.STEAM));
        Assert.Equal(AdmissionMode.STEAM, Assert.Single(f.Accepted));
        Assert.Equal(0, f.Provider.Ticket.Cancels);
        f.Admission.Reset();
        f.Admission.Reset();
        Assert.Equal(1, f.Provider.Ticket.Cancels);
    }

    [Fact]
    public void GuestDoesNotRequestTicketAndCannotUpgradeLater()
    {
        using Fixture f = new();
        f.Provider.Available = false;
        f.Begin();
        Assert.False(Assert.Single(f.Hellos).Steam);
        f.Admission.Offer(new AdmissionOffer(1, AdmissionMode.GUEST, 0), 0);
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.GUEST));
        f.Provider.Available = true;
        f.Admission.Advance(100_000);
        Assert.Equal(AdmissionMode.GUEST, f.Admission.AcceptedMode);
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void KnownBrowserRecipientMustMatchBeforeTicketCreation()
    {
        using Fixture f = new();
        f.Begin(known: 100);
        f.Offer();
        Assert.Equal(AdmissionRejection.RECIPIENT_MISMATCH, Assert.Single(f.Rejected));
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void KnownBrowserRecipientCanMatch()
    {
        using Fixture f = new();
        f.Begin(known: 99);
        f.Offer();
        Assert.Equal(99UL, f.Provider.Recipient);
        Assert.Empty(f.Rejected);
    }

    [Fact]
    public void SteamOfferWithMissingRecipientNeverIssuesUnboundTicket()
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Offer(new AdmissionOffer(1, AdmissionMode.STEAM, 0), 0);
        Assert.Equal(AdmissionRejection.AUTHENTICATION_UNAVAILABLE, Assert.Single(f.Rejected));
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void SteamOfferCannotOverrideGuestCapability()
    {
        using Fixture f = new();
        f.Provider.Available = false;
        f.Begin();
        f.Provider.Available = true;
        f.Offer();
        Assert.Equal(AdmissionRejection.AUTHENTICATION_UNAVAILABLE, Assert.Single(f.Rejected));
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void AdmissionAndTicketDeadlinesAreExact()
    {
        using Fixture f = new();
        f.Begin();
        f.Offer();
        f.Admission.Advance(AdmissionLimits.TICKET_MS - 1);
        Assert.Empty(f.Rejected);
        f.Admission.Advance(AdmissionLimits.TICKET_MS);
        Assert.Equal(AdmissionRejection.TICKET_TIMEOUT, Assert.Single(f.Rejected));
        Assert.Equal(1, f.Provider.Ticket.Cancels);
        f.Admission.Advance(AdmissionLimits.CLIENT_MS);
        Assert.Single(f.Rejected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverallDeadlineAppliesToOfferAndAcceptanceWaits(bool offered)
    {
        using Fixture f = new();
        f.Begin();
        if (offered)
        {
            f.Admission.Offer(new AdmissionOffer(1, AdmissionMode.GUEST, 0), 0);
        }
        f.Admission.Advance(AdmissionLimits.CLIENT_MS - 1);
        Assert.Empty(f.Rejected);
        f.Admission.Advance(AdmissionLimits.CLIENT_MS);
        Assert.Equal(AdmissionRejection.ADMISSION_TIMEOUT, Assert.Single(f.Rejected));
    }

    [Fact]
    public void LateOfferCannotCreateTicketBeforeDeadlineTick()
    {
        using Fixture f = new();
        f.Begin();
        f.Now = AdmissionLimits.CLIENT_MS;
        f.Offer();
        Assert.Equal(AdmissionRejection.ADMISSION_TIMEOUT, Assert.Single(f.Rejected));
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void LateAcceptanceCannotBeatDeadlineTick()
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Offer(new AdmissionOffer(1, AdmissionMode.GUEST, 0), 0);
        f.Now = AdmissionLimits.CLIENT_MS;
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.GUEST));
        Assert.Equal(AdmissionRejection.ADMISSION_TIMEOUT, Assert.Single(f.Rejected));
        Assert.Empty(f.Accepted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void InvalidTicketLengthRejectsWithoutSending(int length)
    {
        using Fixture f = new();
        f.Begin();
        f.Provider.Ticket.Bytes = new byte[length];
        f.Provider.Ticket.State = ClientTicketState.READY;
        f.Offer();
        f.Admission.Advance(0);
        Assert.Equal(AdmissionRejection.TICKET_FAILED, Assert.Single(f.Rejected));
        Assert.Empty(f.Proofs);
        Assert.Equal(1, f.Provider.Ticket.Cancels);
    }

    [Fact]
    public void InvalidTicketAccountRejectsWithoutSending()
    {
        using Fixture f = new();
        f.Begin();
        f.Provider.Ticket.AccountId = 0;
        f.Provider.Ticket.State = ClientTicketState.READY;
        f.Offer();
        f.Admission.Advance(0);
        Assert.Equal(AdmissionRejection.TICKET_FAILED, Assert.Single(f.Rejected));
        Assert.Empty(f.Proofs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationFailureIsTerminal(bool throws)
    {
        using Fixture f = new();
        f.Provider.CreationFails = !throws;
        f.Provider.CreationThrows = throws;
        f.Begin();
        f.Offer();
        Assert.Equal(AdmissionRejection.TICKET_FAILED, Assert.Single(f.Rejected));
        f.Offer();
        f.Admission.Advance(1);
        Assert.Equal(1, f.Provider.Created);
        Assert.Empty(f.Proofs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadinessFailureOrCapabilityLossCancelsTicket(bool unavailable)
    {
        using Fixture f = new();
        f.Begin();
        f.Offer();
        if (unavailable)
        {
            f.Provider.Available = false;
        }
        else
        {
            f.Provider.Ticket.State = ClientTicketState.FAILED;
        }
        f.Admission.Advance(1);
        Assert.Equal(AdmissionRejection.TICKET_FAILED, Assert.Single(f.Rejected));
        Assert.Equal(1, f.Provider.Ticket.Cancels);
    }

    [Fact]
    public void ServerRejectionCancelsExactlyOnceAndDoesNotRetryProof()
    {
        using Fixture f = new();
        f.Begin();
        f.Offer();
        f.Provider.Ticket.State = ClientTicketState.READY;
        f.Admission.Advance(1);
        f.Admission.Reject(new AdmissionRejected(1, AdmissionRejection.VERIFICATION_FAILED));
        f.Admission.Reject(new AdmissionRejected(1, AdmissionRejection.VERIFICATION_FAILED));
        f.Admission.Advance(2);
        Assert.Single(f.Rejected);
        Assert.Single(f.Proofs);
        Assert.Equal(1, f.Provider.Ticket.Cancels);
    }

    [Fact]
    public void ReconnectOwnsFreshTicketAndOldReadinessCannotSendProof()
    {
        using Fixture f = new();
        f.Begin();
        f.Offer();
        Ticket old = f.Provider.Ticket;
        f.Admission.Reset();
        f.Provider.Ticket = new Ticket();
        f.Begin();
        f.Offer();
        old.State = ClientTicketState.READY;
        f.Admission.Advance(1);
        Assert.Empty(f.Proofs);
        f.Provider.Ticket.State = ClientTicketState.READY;
        f.Admission.Advance(2);
        Assert.Single(f.Proofs);
        Assert.Equal(2, f.Provider.Created);
        Assert.Equal(1, old.Cancels);
        Assert.Equal(0, f.Provider.Ticket.Cancels);
    }

    [Theory]
    [InlineData(0, AdmissionMode.GUEST, 0UL)]
    [InlineData(2, AdmissionMode.STEAM, 99UL)]
    [InlineData(1, (AdmissionMode)99, 99UL)]
    [InlineData(1, AdmissionMode.GUEST, 99UL)]
    public void InvalidOfferIsRejected(int attempt, AdmissionMode mode, ulong recipient)
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Offer(new AdmissionOffer(attempt, mode, recipient), 0);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejected));
        Assert.Equal(0, f.Provider.Created);
    }

    [Fact]
    public void AcceptanceWithoutOfferCannotOpenGameplay()
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.GUEST));
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejected));
        Assert.Empty(f.Accepted);
    }

    [Theory]
    [InlineData(2, AdmissionMode.GUEST)]
    [InlineData(1, AdmissionMode.STEAM)]
    public void AcceptanceMustMatchOffer(int attempt, AdmissionMode mode)
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Offer(new AdmissionOffer(1, AdmissionMode.GUEST, 0), 0);
        f.Admission.Accept(new AdmissionAccepted(attempt, mode));
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejected));
    }

    [Fact]
    public void ProofMustBeSentBeforeSteamAcceptance()
    {
        using Fixture f = new();
        f.Begin();
        f.Offer();
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.STEAM));
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejected));
        Assert.Equal(1, f.Provider.Ticket.Cancels);
    }

    [Fact]
    public void RejectionMayArriveBeforeOfferForInvalidHello()
    {
        using Fixture f = new();
        f.Begin();
        f.Admission.Reject(new AdmissionRejected(1, AdmissionRejection.INCOMPATIBLE));
        Assert.Equal(AdmissionRejection.INCOMPATIBLE, Assert.Single(f.Rejected));
    }

    [Fact]
    public void RejectedAndIdleConnectionsIgnoreLateAdmissionMessages()
    {
        using Fixture f = new();
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.GUEST));
        f.Offer();
        Assert.Empty(f.Rejected);
        f.Begin();
        f.Admission.Reject(new AdmissionRejected(0, AdmissionRejection.SERVER_STOPPED));
        f.Offer();
        f.Admission.Accept(new AdmissionAccepted(1, AdmissionMode.GUEST));
        Assert.Single(f.Rejected);
        Assert.Empty(f.Accepted);
    }

    public class Ticket : IClientTicket
    {
        public ClientTicketState State { get; set; }
        public ulong AccountId { get; set; } = 10;
        public ReadOnlyMemory<byte> Bytes { get; set; } = new byte[] { 1, 2, 3 };
        public int Cancels;
        public void Dispose() => Cancels++;
    }

    public class Provider : IClientTicketProvider
    {
        public bool Available { get; set; } = true;
        public Ticket Ticket = new();
        public int Created;
        public ulong Recipient;
        public bool CreationFails;
        public bool CreationThrows;
        public IClientTicket? Create(ulong recipient)
        {
            Created++;
            Recipient = recipient;
            if (CreationThrows)
            {
                throw new InvalidOperationException("No ticket");
            }
            return CreationFails ? null : Ticket;
        }
    }

    public class Fixture : IClientAdmissionTransport, IDisposable
    {
        public readonly Provider Provider = new();
        public readonly ClientAdmission Admission;
        public readonly List<AdmissionHello> Hellos = [];
        public readonly List<SteamProof> Proofs = [];
        public readonly List<AdmissionMode> Accepted = [];
        public readonly List<AdmissionRejection> Rejected = [];
        public ulong Now;
        public Fixture()
        {
            Admission = new ClientAdmission(Provider, this, () => Now);
            Admission.Accepted += Accepted.Add;
            Admission.Rejected += Rejected.Add;
        }
        public void Begin(ulong known = 0) => Admission.Begin("name", 0, Now, known);
        public void Offer() => Admission.Offer(new AdmissionOffer(1, AdmissionMode.STEAM, 99), Now);
        public void Hello(AdmissionHello hello) => Hellos.Add(hello);
        public void Proof(SteamProof proof) => Proofs.Add(proof);
        public void Dispose() => Admission.Dispose();
    }
}
