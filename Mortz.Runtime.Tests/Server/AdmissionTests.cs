using Mortz.Core.Identity;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Mortz.Server.Admission;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class AdmissionTests
{
    [Fact]
    public void GuestAcceptancePrecedesGameplayAndDoesNotAcquireVerification()
    {
        using Fixture f = new();
        f.Backend.Available = false;
        f.Join(2);
        Assert.Equal(new[] { "offer:GUEST", "accepted", "gameplay" }, f.Order);
        Assert.Null(Assert.Single(f.Players).Account);
        Assert.Equal(1, f.Admission.AdmittedCount);
        Assert.Equal(0, f.Admission.ReservedCount);
        Assert.Empty(f.Backend.Started);
    }

    [Fact]
    public void ImmediateSdkSuccessOnlyStartsVerification()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, ulong.MaxValue);
        Assert.Empty(f.Players);
        Assert.Equal(1, f.Admission.ReservedCount);
        f.Complete(ulong.MaxValue, true);
        Assert.Equal(new VerifiedAccount(AccountProvider.STEAM, ulong.MaxValue), Assert.Single(f.Players).Account);
        Assert.Equal(new[] { "offer:STEAM", "accepted", "gameplay" }, f.Order);
        f.Complete(ulong.MaxValue, true);
        Assert.Single(f.Players);
    }

    [Theory]
    [InlineData(false, 99UL)]
    [InlineData(true, 0UL)]
    public void MissingCapabilityOrRecipientSelectsGuest(bool available, ulong recipient)
    {
        using Fixture f = new();
        f.Backend.Available = available;
        f.Backend.ServerAccountId = recipient;
        f.Join(2);
        Assert.Equal(AdmissionMode.GUEST, Assert.Single(f.Offers).Mode);
        Assert.Equal(0UL, Assert.Single(f.Offers).ServerAccountId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    [InlineData(-1)]
    public void MalformedProofDoesNotReachBackend(int size)
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Proof(2, new SteamProof(1, 10, size < 0 ? null! : new byte[size]), f.Now);
        Assert.Equal(AdmissionRejection.INVALID_PROOF, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Backend.Started);
        Assert.Equal(0, f.Admission.ReservedCount);
    }

    [Fact]
    public void ZeroAccountIsRejected()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 0);
        Assert.Equal(AdmissionRejection.INVALID_PROOF, Assert.Single(f.Rejections).Reason);
    }

    [Fact]
    public void MaximumTicketIsNotTruncated()
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Proof(2, new SteamProof(1, 10, new byte[AdmissionLimits.TICKET_BYTES]), f.Now);
        Assert.Equal(AdmissionLimits.TICKET_BYTES, f.Backend.LastLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrongAttemptRejectsWithoutVerification(int attempt)
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Proof(2, new SteamProof(attempt, 10, [1]), f.Now);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Backend.Started);
    }

    [Fact]
    public void ProofBeforeHelloIsRejected()
    {
        using Fixture f = new();
        f.Admission.Connected(2, f.Now);
        f.Proof(2, 10);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateProofEndsOwnedSessionOnce(bool admitted)
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        if (admitted)
        {
            f.Complete(10, true);
        }
        f.Proof(2, 10);
        f.Admission.Disconnected(2);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
        Assert.Equal(new[] { 10UL }, f.Backend.Ended);
        Assert.Equal(admitted ? new[] { 2 } : [], f.Departed);
    }

    [Fact]
    public void DuplicateHelloDoesNotReopenAnAttempt()
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Hello(2, Fixture.Hello, f.Now);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
        Assert.Single(f.Offers);
        Assert.Equal(0, f.Admission.ReservedCount);
    }

    [Fact]
    public void ImmediateVerificationFailureHasNoSessionToEnd()
    {
        using Fixture f = new();
        f.Backend.BeginSucceeds = false;
        f.Join(2);
        f.Proof(2, 10);
        Assert.Equal(AdmissionRejection.VERIFICATION_FAILED, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Backend.Ended);
        Assert.Equal(0, f.Admission.ReservedCount);
    }

    [Fact]
    public void RejectedVerificationReleasesSessionAndReservation()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Complete(10, false);
        Assert.Equal(AdmissionRejection.VERIFICATION_FAILED, Assert.Single(f.Rejections).Reason);
        Assert.Equal(new[] { 10UL }, f.Backend.Ended);
        Assert.Equal(0, f.Admission.ReservedCount);
        Assert.Empty(f.Players);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateValidatingOrAdmittedAccountCannotTakeAnotherSlot(bool admitted)
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        if (admitted)
        {
            f.Complete(10, true);
        }
        f.Join(3);
        f.Proof(3, 10);
        Assert.Equal(AdmissionRejection.DUPLICATE_ACCOUNT, Assert.Single(f.Rejections).Reason);
        Assert.Single(f.Backend.Started);
        Assert.Empty(f.Backend.Ended);
        Assert.Equal(1, f.Admission.ReservedCount + f.Admission.AdmittedCount);
    }

    [Theory]
    [InlineData(0, AdmissionRejection.HELLO_TIMEOUT)]
    [InlineData(1, AdmissionRejection.PROOF_TIMEOUT)]
    [InlineData(2, AdmissionRejection.VERIFICATION_TIMEOUT)]
    public void DeadlinesExpireExactlyAtBoundary(int stage, AdmissionRejection expected)
    {
        using Fixture f = new();
        f.Admission.Connected(2, f.Now);
        if (stage >= 1)
        {
            f.Admission.Hello(2, Fixture.Hello, f.Now);
        }
        if (stage >= 2)
        {
            f.Proof(2, 10);
        }
        ulong timeout = stage switch { 0 => AdmissionLimits.HELLO_MS, 1 => AdmissionLimits.PROOF_MS, _ => AdmissionLimits.VERIFICATION_MS };
        f.Now = timeout - 1;
        f.Admission.Advance(f.Now);
        Assert.Empty(f.Rejections);
        f.Now++;
        f.Admission.Advance(f.Now);
        Assert.Equal(expected, Assert.Single(f.Rejections).Reason);
        Assert.Equal(0, f.Admission.ReservedCount);
        Assert.Equal(stage == 2 ? new[] { 10UL } : [], f.Backend.Ended);
    }

    [Fact]
    public void LateCallbackCannotBeatDeadlineTick()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Now = AdmissionLimits.VERIFICATION_MS;
        f.Complete(10, true);
        Assert.Empty(f.Players);
        Assert.Equal(AdmissionRejection.VERIFICATION_TIMEOUT, Assert.Single(f.Rejections).Reason);
    }

    [Fact]
    public void LateHelloAndProofCannotBeatDeadlineTick()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello, AdmissionLimits.HELLO_MS);
        Assert.Equal(AdmissionRejection.HELLO_TIMEOUT, Assert.Single(f.Rejections).Reason);
        f.Join(3);
        f.Admission.Proof(3, new SteamProof(1, 10, [1]), AdmissionLimits.PROOF_MS);
        Assert.Equal(AdmissionRejection.PROOF_TIMEOUT, f.Rejections[1].Reason);
    }

    [Fact]
    public void AdmittedAndReservedSlotsTogetherCannotExceedCapacity()
    {
        using Fixture f = new();
        for (int peer = 2; peer < AdmissionLimits.RESERVATIONS + 2; peer++)
        {
            f.Join(peer);
            if (peer % 2 == 0)
            {
                f.Proof(peer, (ulong)peer);
                f.Complete((ulong)peer, true);
            }
        }
        f.Join(100);
        Assert.Equal(AdmissionRejection.SERVER_FULL, Assert.Single(f.Rejections).Reason);
        Assert.Equal(AdmissionLimits.RESERVATIONS, f.Admission.AdmittedCount + f.Admission.ReservedCount);
        f.Admission.Disconnected(3);
        f.Join(101);
        Assert.Equal(AdmissionLimits.RESERVATIONS, f.Admission.AdmittedCount + f.Admission.ReservedCount);
    }

    [Fact]
    public void RevocationDisconnectsAcceptedAccountWithoutDowngrade()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Complete(10, true);
        f.Complete(10, false);
        Assert.Equal(AdmissionRejection.REVOKED, Assert.Single(f.Rejections).Reason);
        Assert.Equal(new[] { 2 }, f.Departed);
        Assert.Equal(new[] { 10UL }, f.Backend.Ended);
        Assert.Equal(0, f.Admission.AdmittedCount);
    }

    [Fact]
    public void CapabilityLossEndsPendingAndVerifiedSessionsButKeepsGuests()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Complete(10, true);
        f.Join(3);
        f.Proof(3, 11);
        f.Join(4);
        f.Admission.Connected(5, 0);
        f.Admission.Hello(5, Fixture.Hello with { Steam = false }, 0);
        f.Backend.Available = false;
        f.Admission.Advance(1);
        Assert.Equal(3, f.Rejections.Count);
        Assert.All(f.Rejections, rejection => Assert.Equal(AdmissionRejection.AUTHENTICATION_UNAVAILABLE, rejection.Reason));
        Assert.Equal(new[] { 10UL, 11UL }, f.Backend.Ended);
        Assert.Equal(1, f.Admission.AdmittedCount);
        f.Join(6);
        Assert.Null(f.Players.Last().Account);
        f.Backend.Available = true;
        f.Admission.Advance(2);
        Assert.Equal(2, f.Admission.AdmittedCount);
    }

    [Fact]
    public void DisconnectAndRapidReconnectDrainOldCallbacksBeforeReuse()
    {
        using Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Admission.Disconnected(2);
        f.Join(3);
        f.Proof(3, 10);
        Assert.Equal(AdmissionRejection.DUPLICATE_ACCOUNT, Assert.Single(f.Rejections).Reason);
        f.Sessions.Pump(() => { f.Sessions.Result(10, true); f.Sessions.Result(10, false); });
        Assert.Empty(f.Players);
        f.Join(4);
        f.Proof(4, 10);
        f.Complete(10, true);
        Assert.Equal(4, Assert.Single(f.Players).PeerId);
        Assert.Equal(2, f.Backend.Started.Count);
    }

    [Fact]
    public void MortzGenerationRejectsDelayedContinuationAfterPeerReuse()
    {
        ManualVerifier verifier = new();
        RecordingTransport transport = new();
        using ServerAdmission admission = new(verifier, transport, () => 0);
        List<AdmittedPlayer> players = [];
        admission.Admitted += players.Add;
        admission.Connected(2, 0);
        admission.Hello(2, Fixture.Hello, 0);
        admission.Proof(2, new SteamProof(1, 10, [1]), 0);
        long old = verifier.LastGeneration;
        admission.Disconnected(2);
        admission.Connected(2, 0);
        admission.Hello(2, Fixture.Hello, 0);
        admission.Proof(2, new SteamProof(1, 10, [1]), 0);
        verifier.Complete(new VerificationResult(old, 10, true));
        Assert.Empty(players);
        verifier.Complete(new VerificationResult(verifier.LastGeneration, 10, true));
        Assert.Single(players);
    }

    [Fact]
    public void DisposeIsIdempotentAndStopsNewAdmission()
    {
        Fixture f = new();
        f.Join(2);
        f.Proof(2, 10);
        f.Admission.Dispose();
        f.Admission.Dispose();
        f.Sessions.Result(10, true);
        f.Admission.Connected(3, 0);
        Assert.Equal(new[] { 10UL }, f.Backend.Ended);
        Assert.Equal(0, f.Admission.ReservedCount);
        Assert.Empty(f.Players);
        Assert.All(f.Rejections, item => Assert.Equal(AdmissionRejection.SERVER_STOPPED, item.Reason));
        f.Dispose();
    }

    [Theory]
    [InlineData(1, 0, "name", 0, AdmissionRejection.INCOMPATIBLE)]
    [InlineData(47, 0, "name", 0, AdmissionRejection.INCOMPATIBLE)]
    public void IncompatibleHelloIsTerminal(int protocol, ulong schema, string name, int skin, AdmissionRejection reason)
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, new AdmissionHello(protocol, schema, name, skin, true), 0);
        Assert.Equal(reason, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Offers);
    }

    [Theory]
    [InlineData(-1, "name")]
    [InlineData(999, "name")]
    [InlineData(0, "1234567890123456789012345")]
    [InlineData(0, null)]
    public void InvalidProfileIsRejectedBeforeReservation(int skin, string? name)
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello with { Skin = skin, Name = name! }, 0);
        Assert.Equal(AdmissionRejection.INVALID_PROFILE, Assert.Single(f.Rejections).Reason);
        Assert.Equal(0, f.Admission.ReservedCount);
    }

    public class Backend : IVerificationBackend
    {
        public bool Available { get; set; } = true;
        public ulong ServerAccountId { get; set; } = 99;
        public bool BeginSucceeds { get; set; } = true;
        public List<ulong> Started { get; } = [];
        public List<ulong> Ended { get; } = [];
        public int LastLength;
        public bool Begin(byte[] ticket, ulong accountId)
        {
            Started.Add(accountId);
            LastLength = ticket.Length;
            return BeginSucceeds;
        }
        public void End(ulong accountId) => Ended.Add(accountId);
    }

    public class RecordingTransport : IServerAdmissionTransport
    {
        public readonly List<string> Order = [];
        public readonly List<AdmissionOffer> Offers = [];
        public readonly List<AdmissionRejected> Rejections = [];
        public void Offer(int peerId, AdmissionOffer offer) { Offers.Add(offer); Order.Add($"offer:{offer.Mode}"); }
        public void Accept(int peerId, AdmissionAccepted accepted) => Order.Add("accepted");
        public void Reject(int peerId, AdmissionRejected rejected) => Rejections.Add(rejected);
    }

    public class Fixture : RecordingTransport, IDisposable
    {
        public static readonly AdmissionHello Hello = new(NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, "player", 0, true);
        public readonly Backend Backend = new();
        public readonly VerificationSessions Sessions;
        public readonly ServerAdmission Admission;
        public readonly List<AdmittedPlayer> Players = [];
        public readonly List<int> Departed = [];
        public ulong Now;
        public Fixture()
        {
            Sessions = new VerificationSessions(Backend);
            Admission = new ServerAdmission(Sessions, this, () => Now);
            Admission.Admitted += player => { Players.Add(player); Order.Add("gameplay"); };
            Admission.Departed += Departed.Add;
        }
        public void Join(int peerId) { Admission.Connected(peerId, Now); Admission.Hello(peerId, Hello, Now); }
        public void Proof(int peerId, ulong account) => Admission.Proof(peerId, new SteamProof(1, account, [1, 2, 3]), Now);
        public void Complete(ulong account, bool accepted) => Sessions.Pump(() => Sessions.Result(account, accepted));
        public void Dispose() { Admission.Dispose(); Sessions.Dispose(); }
    }

    public class ManualVerifier : IAdmissionVerifier
    {
        public bool Available => true;
        public ulong ServerAccountId => 99;
        public long LastGeneration;
        public event Action<VerificationResult>? Completed;
        public bool TryBegin(long generation, ulong accountId, byte[] ticket, out AdmissionRejection reason)
        { LastGeneration = generation; reason = default; return true; }
        public void End(long generation) { }
        public void Complete(VerificationResult result) => Completed?.Invoke(result);
    }
}
