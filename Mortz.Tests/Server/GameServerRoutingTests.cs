using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>What Receive does with traffic that is not a live, well-formed
/// message from a known player.</summary>
public class GameServerRoutingTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public void AMessageFromAPeerWhoNeverJoinedIsDropped()
    {
        _server.Receive(99, new ChatSendMsg("hello"));

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void AMessageFromAPeerWhoAlreadyLeftIsDropped()
    {
        _server.Connect(7, "alice");
        _server.Server.Disconnect(7);
        _server.Link.Messages.Clear();

        _server.Receive(7, new ChatSendMsg("hello"));

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void AMalformedPayloadIsDroppedWithoutThrowing()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Server.Receive(7, ChatSendMsg.MsgId, [1, 2, 3]);

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void AnUnknownMessageIdIsDroppedWithoutThrowing()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Server.Receive(7, ushort.MaxValue, []);

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void AServerToClientIdIsNotRoutedBack()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Server.Receive(7, NetRegistry.ID_ChatMsg,
            ChatSendMsg.Serialize(new ChatSendMsg("hello")));

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void AMessageWithALiveHandlerIsAnsweredOnTheWire()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Receive(7, new ChatSendMsg("hello"));

        ChatMsg line = _server.Link.Last<ChatMsg>();
        Assert.Equal("hello", line.Text);
        Assert.Equal(7, line.SenderId);
    }

    [Fact]
    public void ARepeatedTypingStateIsNotRebroadcast()
    {
        _server.Connect(7, "alice");
        _server.Receive(7, new TypingMsg(true));
        _server.Link.Messages.Clear();

        _server.Receive(7, new TypingMsg(true));

        Assert.Empty(_server.Link.Messages);
    }
}
