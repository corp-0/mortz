using System.Text;
using Chickensoft.AutoInject;
using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Session;
using Mortz.Core.Admin;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Core.Net.Admin;
using Mortz.Core.Net.Chat;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(MortzGodotCollection))]
public class ClientChatTests : NodeServiceTest
{
    private const int SENDER = 42;

    private readonly ClientChat _chat;
    private readonly ClientAdmin _admin;
    private readonly FakeSessionExit _sessionExit = new();

    public ClientChatTests()
    {
        FakeNetwork network = new() { LocalPeerId = SENDER };
        ClientAdmin admin = new();
        admin.FakeDependency<INetwork>(network);
        admin.FakeDependency(Router);
        _admin = Host(admin);
        ClientChat chat = new();
        chat.FakeDependency(_admin);
        chat.FakeDependency<ISessionExit>(_sessionExit);
        chat.FakeDependency(Router);
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
        Assert.DoesNotContain(_chat.Lines,
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
        Assert.True(Router.Dispatch(NetRegistry.ID_AdminChallengeMsg,
            challengePayload));
        Assert.Equal(NetRegistry.ID_AdminProofMsg, sentId);

        byte[] statePayload = Capture(
            () => new AdminStateMsg(true, "Admin access granted.").SendTo(SENDER));
        Assert.True(Router.Dispatch(NetRegistry.ID_AdminStateMsg,
            statePayload));

        Assert.True(_admin.IsAdmin);
        Assert.True(_admin.TrySignAdminAction(4, [1, 2, 3], out _, out byte[] tag));
        Assert.NotEmpty(tag);
        Assert.Contains(_chat.Lines,
            entry => entry.Text == "Admin access granted.");
    }

    [Fact]
    public void RollLinesBecomeRollEntriesAndOutOfRangeValuesAreDropped()
    {
        byte[] payload = Capture(
            () => ChatProtocol.Encode(new ChatLine.Roll(SENDER, "Alice", 73)).Broadcast());
        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            payload));
        ChatLine.Roll entry = Assert.IsType<ChatLine.Roll>(
            Assert.Single(_chat.Lines));
        Assert.Equal(73, entry.Value);
        Assert.Equal("Alice", entry.SenderName);

        byte[] bogus = ChatPayload(kind: 2, SENDER, "Alice", "999", format: 0);
        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            bogus));
        Assert.Single(_chat.Lines);
    }

    [Fact]
    public void DropsUnknownServerLineKinds()
    {
        byte[] payload = Capture(
            () => ChatProtocol.Encode(
                new ChatLine.Player(SENDER, "Alice", "hello")).Broadcast());
        payload[0] = byte.MaxValue;

        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            payload));
        Assert.Empty(_chat.Lines);
    }

    [Fact]
    public void RichSystemLinesKeepTrustedServerFormatting()
    {
        byte[] payload = Capture(
            () => ChatProtocol.Encode(
                new ChatLine.System(new Mortz.Core.Text.RichText()
                    .Add("changed", new Mortz.Core.Text.Style().Bold()))).Broadcast());

        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            payload));

        ChatLine.System entry = Assert.IsType<ChatLine.System>(
            Assert.Single(_chat.Lines));
        Assert.Equal("[b]changed[/b]", entry.Render().ToString());
    }

    [Fact]
    public void DropsUnsupportedKindAndFormatCombinations()
    {
        byte[] playerPlain = ChatPayload(
            kind: 0, SENDER, "Alice", "hello", format: 0);
        byte[] systemMarkdown = ChatPayload(
            kind: 1, 0, "Server", "**hello**", format: 1);
        byte[] unknownFormat = ChatPayload(
            kind: 1, 0, "Server", "hello", format: byte.MaxValue);

        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            playerPlain));
        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            systemMarkdown));
        Assert.True(Router.Dispatch(NetRegistry.ID_ChatMsg,
            unknownFormat));
        Assert.Empty(_chat.Lines);
    }

    [Fact]
    public void QuitLeavesTheSessionAndTakesNoArguments()
    {
        Assert.False(_chat.Submit("/quit now"));
        Assert.Empty(_sessionExit.Reasons);

        Assert.True(_chat.Submit("/quit"));
        Assert.Equal("Left the server.", Assert.Single(_sessionExit.Reasons));
    }

    private static byte[] Capture(Action send)
    {
        byte[] payload = [];
        NetTransport.Send = (_, bytes, _, _) => payload = bytes;
        send();
        return payload;
    }

    private static byte[] ChatPayload(
        byte kind,
        int senderId,
        string senderName,
        string text,
        byte format)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(kind);
        writer.Write(senderId);
        writer.Write(senderName);
        writer.Write(text);
        writer.Write(format);
        return stream.ToArray();
    }
}
