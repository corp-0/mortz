using System.Security.Cryptography;
using Mortz.Client.Admin;
using Mortz.Core.Admin;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client.Chat;

/// <summary>The admin handshake state machine against captured wire sends;
/// engine-free, so it runs without the Godot fixture.</summary>
[Collection("NetTransport")]
public class AdminAuthFlowTests : IDisposable
{
    private const long PEER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;
    private ushort _sentId;
    private byte[] _sentPayload = [];

    public AdminAuthFlowTests() =>
        NetTransport.Send = (id, payload, _, _) => (_sentId, _sentPayload) = (id, payload);

    public void Dispose() => NetTransport.Send = _original;

    [Fact]
    public void HandshakeDerivesProofAndSignedSessionKey()
    {
        const string PASSWORD = "correct horse battery staple";
        AdminAuthFlow flow = new();
        flow.Begin(PASSWORD);
        Assert.Equal(NetRegistry.ID_AdminAuthRequestMsg, _sentId);

        byte[] sessionId = Enumerable.Repeat((byte)3, AdminCrypto.SESSION_ID_BYTES).ToArray();
        byte[] nonce = Enumerable.Repeat((byte)7, AdminCrypto.NONCE_BYTES).ToArray();
        byte[] challenge = AdminCrypto.BuildChallenge(sessionId, nonce);
        Assert.True(flow.TryAnswerChallenge(PEER, new AdminChallengeMsg(challenge)));
        Assert.Equal(NetRegistry.ID_AdminProofMsg, _sentId);

        AdminProofMsg receivedProof = default;
        Action<long, AdminProofMsg> proofHandler = (_, message) => receivedProof = message;
        AdminProofMsg.Received += proofHandler;
        try
        {
            Assert.True(NetRegistry.Dispatch(NetRegistry.ID_AdminProofMsg, PEER,
                _sentPayload, isServer: true));
        }
        finally
        {
            AdminProofMsg.Received -= proofHandler;
        }
        byte[] passwordKey = AdminCrypto.DerivePasswordKey(PASSWORD);
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
        AdminAuthFlow flow = new();
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
        AdminAuthFlow flow = new();
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
}
