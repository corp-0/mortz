using System.Security.Cryptography;
using System.Text;
using Mortz.Client.Admin;
using Mortz.Core.Admin;
using Mortz.Core.Net;
using Mortz.Core.Net.Admin;
using Mortz.Tests.Core.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client.Chat;

/// <summary>The admin handshake state machine against captured wire sends.</summary>
public class AdminAuthFlowTests
{
    private const int PEER = 42;

    private readonly TestClientSender _sender = new();

    [Fact]
    public void HandshakeDerivesProofAndSignedSessionKey()
    {
        const string PASSWORD = "correct horse battery staple";
        AdminAuthFlow flow = new(_sender);
        flow.Begin(PASSWORD);
        Assert.Equal(NetRegistry.ID_AdminAuthRequestMsg, Assert.Single(_sender.Sent).Id);

        byte[] sessionId = Enumerable.Repeat((byte)3, AdminCrypto.SESSION_ID_BYTES).ToArray();
        byte[] nonce = Enumerable.Repeat((byte)7, AdminCrypto.NONCE_BYTES).ToArray();
        byte[] challenge = AdminCrypto.BuildChallenge(sessionId, nonce);
        Assert.True(flow.TryAnswerChallenge(PEER, new AdminChallengeMsg(challenge)));
        Assert.Equal(NetRegistry.ID_AdminProofMsg, _sender.Sent[^1].Id);

        NetRouter<int> router = new();
        Probe<AdminProofMsg> proofProbe = new();
        router.Add(proofProbe);
        Assert.True(router.Dispatch(NetRegistry.ID_AdminProofMsg, PEER,
            _sender.Sent[^1].Payload));
        AdminProofMsg receivedProof = Assert.Single(proofProbe.Deliveries).Message;

        byte[] passwordKey = AdminCrypto.DerivePasswordKey(
            Encoding.UTF8.GetBytes(PASSWORD), challenge);
        Assert.Equal(AdminCrypto.ComputeProof(passwordKey, PEER, challenge),
            receivedProof.Proof);

        flow.ApplyState(new AdminStateMsg(true, "Admin access granted."));
        Assert.True(flow.IsAdmin);
        Assert.True(flow.TrySign(PEER, 4, [1, 2, 3], out ulong sequence, out byte[] tag));
        byte[] sessionKey = AdminCrypto.DeriveSessionKey(passwordKey, PEER, challenge);
        Assert.Equal(AdminCrypto.ComputeCommandTag(sessionKey, PEER, sequence, 4, [1, 2, 3]),
            tag);
        CryptographicOperations.ZeroMemory(passwordKey);
        CryptographicOperations.ZeroMemory(sessionKey);
    }

    [Fact]
    public void MalformedChallengeDropsThePendingAttempt()
    {
        AdminAuthFlow flow = new(_sender);
        flow.Begin("hunter2");

        Assert.False(flow.TryAnswerChallenge(PEER, new AdminChallengeMsg([1, 2, 3])));

        // A well-formed challenge arriving after the drop finds nothing pending.
        byte[] challenge = AdminCrypto.BuildChallenge(
            new byte[AdminCrypto.SESSION_ID_BYTES], new byte[AdminCrypto.NONCE_BYTES]);
        Assert.False(flow.TryAnswerChallenge(PEER, new AdminChallengeMsg(challenge)));
    }

    [Fact]
    public void RevocationDropsAuthority()
    {
        AdminAuthFlow flow = new(_sender);
        flow.Begin("hunter2");
        byte[] challenge = AdminCrypto.BuildChallenge(
            new byte[AdminCrypto.SESSION_ID_BYTES], new byte[AdminCrypto.NONCE_BYTES]);
        Assert.True(flow.TryAnswerChallenge(PEER, new AdminChallengeMsg(challenge)));
        flow.ApplyState(new AdminStateMsg(true, "granted"));
        Assert.True(flow.IsAdmin);

        flow.ApplyState(new AdminStateMsg(false, "revoked"));

        Assert.False(flow.IsAdmin);
        Assert.False(flow.TrySign(PEER, 4, [1, 2, 3], out _, out _));
    }

    [Fact]
    public void FlowsSendOnlyThroughTheirOwnSender()
    {
        TestClientSender firstSender = new();
        TestClientSender secondSender = new();
        AdminAuthFlow first = new(firstSender);
        AdminAuthFlow second = new(secondSender);

        first.Begin("first");

        Assert.Single(firstSender.Sent);
        Assert.Empty(secondSender.Sent);

        second.Begin("second");

        Assert.Single(firstSender.Sent);
        Assert.Single(secondSender.Sent);
    }
}
