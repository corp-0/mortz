using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>The generated router: registration, decode guards, and the boot log line.</summary>
public class NetRouterTests
{
    [Fact]
    public void RegistryNamesMessagesWithoutAManualLookupTable()
    {
        Assert.Equal(nameof(ChatSendMsg), NetRegistry.NameOf(ChatSendMsg.MsgId));
        Assert.Equal("unknown message 65535", NetRegistry.NameOf(ushort.MaxValue));
    }

    private sealed class FirstChatHandler(List<string> log) : IHandle<int, ChatSendMsg>
    {
        public void Handle(int sender, in ChatSendMsg message) =>
            log.Add($"first:{sender}:{message.Text}");
    }

    private sealed class SecondChatHandler(List<string> log) : IHandle<int, ChatSendMsg>
    {
        public void Handle(int sender, in ChatSendMsg message) =>
            log.Add($"second:{sender}:{message.Text}");
    }

    private sealed class ReadyHandler(List<string> log) : IHandle<int, SetReadyMsg>
    {
        public void Handle(int sender, in SetReadyMsg message) => log.Add($"ready:{message.Ready}");
    }

    /// <summary>One object claiming two messages: the router splits it by pattern match.</summary>
    private sealed class BothHandler(List<string> log)
        : IHandle<int, ChatSendMsg>, IHandle<int, SetReadyMsg>
    {
        public void Handle(int sender, in ChatSendMsg message) => log.Add("both:chat");

        public void Handle(int sender, in SetReadyMsg message) => log.Add("both:ready");
    }

    [Fact]
    public void HandlersRunInAddOrder()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new SecondChatHandler(log));
        router.Add(new FirstChatHandler(log));

        Assert.True(router.Dispatch(
            ChatSendMsg.MsgId, 7, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));

        Assert.Equal(["second:7:hi", "first:7:hi"], log);
    }

    [Fact]
    public void OneHandlerServesEveryMessageItImplements()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new BothHandler(log));

        Assert.True(router.Dispatch(
            ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
        Assert.True(router.Dispatch(
            SetReadyMsg.MsgId, 1, SetReadyMsg.Serialize(new SetReadyMsg(true))));

        Assert.Equal(["both:chat", "both:ready"], log);
    }

    private sealed class IoFailingHandler : IHandle<int, ChatSendMsg>
    {
        public void Handle(int sender, in ChatSendMsg message) => throw new IOException("boom");
    }

    private sealed class ThrowingHandler : IHandle<int, ChatSendMsg>
    {
        public void Handle(int sender, in ChatSendMsg message) =>
            throw new InvalidOperationException("handler failed");
    }

    /// <summary>A bug inside a handler must not read as a malformed packet.</summary>
    [Fact]
    public void AHandlerThatThrowsIsNotSwallowed()
    {
        NetRouter<int> router = new();
        router.Add(new ThrowingHandler());

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            router.Dispatch(ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));

        Assert.Equal("handler failed", thrown.Message);
    }

    /// <summary>Decode failures are caught by type, so an IOException from a
    /// handler must not be mistaken for a bad payload either.</summary>
    [Fact]
    public void AHandlerIoFailureIsNotMistakenForAMalformedPayload()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new IoFailingHandler());
        router.Add(new FirstChatHandler(log));

        Assert.Throws<IOException>(() =>
            router.Dispatch(ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
        Assert.Empty(log);
    }

    [Fact]
    public void AMessageWithNoHandlerIsNotDispatched()
    {
        NetRouter<int> router = new();

        Assert.False(router.Dispatch(
            ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
    }

    [Fact]
    public void ClearDropsEveryHandler()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));
        router.Add(new ReadyHandler(log));
        router.Clear();

        Assert.False(router.Dispatch(
            ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
        Assert.False(router.Dispatch(
            SetReadyMsg.MsgId, 1, SetReadyMsg.Serialize(new SetReadyMsg(true))));
        Assert.Empty(log);
    }

    [Fact]
    public void ClearedHandlersDoNotDuplicateAfterARebuild()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));
        router.Clear();
        router.Add(new FirstChatHandler(log));

        Assert.True(router.Dispatch(
            ChatSendMsg.MsgId, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));

        Assert.Equal(["first:1:hi"], log);
    }

    [Fact]
    public void ATruncatedPayloadIsRejected()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));

        Assert.False(router.Dispatch(ChatSendMsg.MsgId, 1, [0x05, 0x68, 0x69]));
        Assert.Empty(log);
    }

    [Fact]
    public void APayloadWithTrailingBytesIsRejected()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new ReadyHandler(log));
        byte[] payload = [.. SetReadyMsg.Serialize(new SetReadyMsg(true)), 0x00];

        Assert.False(router.Dispatch(SetReadyMsg.MsgId, 1, payload));
        Assert.Empty(log);
    }

    [Fact]
    public void AnOversizedPayloadIsRejected()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new ReadyHandler(log));

        Assert.False(router.Dispatch(
            SetReadyMsg.MsgId, 1, new byte[NetConfig.MAX_ENVELOPE_BYTES + 1]));
        Assert.Empty(log);
    }

    [Fact]
    public void AServerToClientIdIsNotRouted()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));

        Assert.False(router.Dispatch(
            NetRegistry.ID_ChatMsg, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
        Assert.Empty(log);
    }

    [Fact]
    public void AnUnknownIdIsNotRouted()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));

        Assert.False(router.Dispatch(
            ushort.MaxValue, 1, ChatSendMsg.Serialize(new ChatSendMsg("hi"))));
        Assert.Empty(log);
    }

    [Fact]
    public void DescribeNamesEveryRoutedMessageAndItsHandlers()
    {
        List<string> log = [];
        NetRouter<int> router = new();
        router.Add(new FirstChatHandler(log));
        router.Add(new SecondChatHandler(log));
        router.Add(new ReadyHandler(log));

        Assert.Equal(
            "router: ChatSendMsg -> FirstChatHandler, SecondChatHandler; SetReadyMsg -> ReadyHandler",
            router.Describe());
    }

    [Fact]
    public void DescribeSaysSoWhenNothingIsRouted()
    {
        NetRouter<int> router = new();

        Assert.Equal("router: nothing routed", router.Describe());
    }
}
