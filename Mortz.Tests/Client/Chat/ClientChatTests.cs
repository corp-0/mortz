using System.Text;
using Chickensoft.AutoInject;
using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Feed;
using Mortz.Core.Admin;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client.Chat;

/// <summary>Chat node behavior over the wire, with a real ClientAdmin faked
/// in as its dependency.</summary>
[Collection(nameof(GodotHeadlessCollection))]
public class ClientChatTests : NodeServiceTest
{
    private const long SENDER = 42;

    private readonly ClientChat _chat;
    private readonly ClientAdmin _admin;

    public ClientChatTests()
    {
        _admin = Host(new ClientAdmin());
        _admin.SetLocalPeerIdForTest(SENDER);
        ClientChat chat = new();
        chat.FakeDependency(_admin);
        _chat = Host(chat);
    }

    [Fact]
    public void AdminCommandNeverSerializesPasswordAsChatOrHistory()
    {
        ushort sentId = 0;
        byte[] sentPayload = [];
        NetTransport.Send = (id, payload, _, _) => (sentId, sentPayload) = (id, payload);

        Assert.True(_chat.Submit("/admin definitely-not-a-chat-secret"));

        Assert.Equal(NetRegistry.ID_AdminAuthRequestMsg, sentId);
        Assert.DoesNotContain("definitely-not-a-chat-secret",
            Encoding.UTF8.GetString(sentPayload));
        Assert.DoesNotContain(_chat.State.Entries,
            entry => entry.Text.Contains("definitely-not-a-chat-secret",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AdminHandshakeOverTheWireGrantsSigningAuthority()
    {
        ushort sentId = 0;
        NetTransport.Send = (id, _, _, _) => sentId = id;
        Assert.True(_chat.Submit("/admin \"correct horse battery staple\""));
        Assert.Equal(NetRegistry.ID_AdminAuthRequestMsg, sentId);

        byte[] challenge = AdminCrypto.BuildChallenge(
            Enumerable.Repeat((byte)3, AdminCrypto.SESSION_ID_BYTES).ToArray(),
            Enumerable.Repeat((byte)7, AdminCrypto.NONCE_BYTES).ToArray());
        byte[] challengePayload = Capture(() => new AdminChallengeMsg(challenge).SendTo(SENDER));
        NetTransport.Send = (id, _, _, _) => sentId = id;
        Assert.True(NetRegistry.Dispatch(NetRegistry.ID_AdminChallengeMsg, SENDER,
            challengePayload, isServer: false));
        Assert.Equal(NetRegistry.ID_AdminProofMsg, sentId);

        byte[] statePayload = Capture(
            () => new AdminStateMsg(true, "Admin access granted.").SendTo(SENDER));
        Assert.True(NetRegistry.Dispatch(NetRegistry.ID_AdminStateMsg, SENDER,
            statePayload, isServer: false));

        Assert.True(_admin.IsAdmin);
        Assert.True(_admin.TrySignAdminAction(4, [1, 2, 3], out _, out byte[] tag));
        Assert.NotEmpty(tag);
        Assert.Contains(_chat.State.Entries,
            entry => entry.Text == "Admin access granted.");
    }

    [Fact]
    public void RollLinesBecomeRollEntriesAndOutOfRangeValuesAreDropped()
    {
        byte[] payload = Capture(
            () => new ChatLineMsg(ChatLineKind.ROLL, SENDER, "Alice", "73").Broadcast());
        Assert.True(NetRegistry.Dispatch(NetRegistry.ID_ChatLineMsg, SENDER,
            payload, isServer: false));
        ChatEntry entry = Assert.Single(_chat.State.Entries);
        Assert.Equal(ChatEntryKind.ROLL, entry.Kind);
        Assert.Equal("73", entry.Text);
        Assert.Equal("Alice", entry.SenderName);

        byte[] bogus = Capture(
            () => new ChatLineMsg(ChatLineKind.ROLL, SENDER, "Alice", "999").Broadcast());
        Assert.True(NetRegistry.Dispatch(NetRegistry.ID_ChatLineMsg, SENDER,
            bogus, isServer: false));
        Assert.Single(_chat.State.Entries);
    }

    [Fact]
    public void DropsUnknownServerLineKinds()
    {
        byte[] payload = Capture(
            () => new ChatLineMsg(ChatLineKind.PLAYER, SENDER, "Alice", "hello").Broadcast());
        payload[0] = byte.MaxValue;

        Assert.True(NetRegistry.Dispatch(NetRegistry.ID_ChatLineMsg, SENDER,
            payload, isServer: false));
        Assert.Empty(_chat.State.Entries);
    }

    [Fact]
    public void KillFeedLinesLandInChatAsSystemEntries()
    {
        FakeKillFeed feed = new();
        ClientChat chat = new();
        chat.FakeDependency(_admin);
        chat.FakeDependency<IKillFeed>(feed);
        Host(chat);

        feed.Emit("Alice killed Bob");

        ChatEntry entry = Assert.Single(chat.State.Entries);
        Assert.Equal(ChatEntryKind.SYSTEM, entry.Kind);
        Assert.Equal("Alice killed Bob", entry.Text);
    }

    private sealed class FakeKillFeed : IKillFeed
    {
        public event Action<string>? LineAdded;

        public void Emit(string line) => LineAdded?.Invoke(line);
    }

    private static byte[] Capture(Action send)
    {
        byte[] payload = [];
        NetTransport.Send = (_, bytes, _, _) => payload = bytes;
        send();
        return payload;
    }
}
