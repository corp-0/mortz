using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Core.Chat;
using Mortz.Core.Net;
using Mortz.Net;
using Xunit;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(MortzGodotCollection))]
public class ChatFeedTests : NodeServiceTest
{
    private readonly ClientChat _chat;
    private readonly ChatFeed _feed;

    public ChatFeedTests()
    {
        ClientAdmin admin = new();
        admin.FakeDependency<INetwork>(new FakeNetwork());
        ClientChat chat = new();
        chat.FakeDependency(Host(admin));
        _chat = Host(chat);
        _feed = Host(new ChatFeed());
        _feed.Bind(_chat);
    }

    [Fact]
    public void LiveEntriesAppendLinesAndRaiseLineAdded()
    {
        Control? added = null;
        _feed.LineAdded += line => added = line;

        _chat.AddSystem("hello");

        Assert.Equal(1, _feed.GetChildCount());
        Assert.Same(_feed.GetChild(0), added);
    }

    [Fact]
    public void BindingRebuildsHistoryWithoutLineAddedEvents()
    {
        _chat.AddSystem("one");
        _chat.AddSystem("two");

        ChatFeed late = Host(new ChatFeed());
        int liveLines = 0;
        bool rebuilt = false;
        late.LineAdded += _ => liveLines++;
        late.Rebuilt += () => rebuilt = true;
        late.Bind(_chat);

        Assert.True(rebuilt);
        Assert.Equal(0, liveLines);
        Assert.Equal(2, late.GetChildCount());
    }

    [Fact]
    public void RollEntriesRenderAsRollLines()
    {
        _chat.Add(new ChatLine.Roll(1, "Alice", 42));

        Assert.IsType<RollLine>(_feed.GetChild(0));
    }

    [Fact]
    public void HistoryCapTrimsTheOldestLine()
    {
        _chat.AddSystem("first");
        int afterOne = _feed.GetChildCount();
        for (int i = 1; i < NetConfig.MAX_CHAT_HISTORY; i++)
        {
            _chat.AddSystem($"line {i}");
        }
        int afterCap = _feed.GetChildCount();
        _chat.AddSystem("overflow");
        int afterOverflow = _feed.GetChildCount();

        Assert.Equal((1, NetConfig.MAX_CHAT_HISTORY, NetConfig.MAX_CHAT_HISTORY),
            (afterOne, afterCap, afterOverflow));
        Assert.Equal(NetConfig.MAX_CHAT_HISTORY, _chat.Lines.Count);
        Assert.Equal("line 1", _chat.Lines[0].Text);
    }

    [Fact]
    public void ClearingChatEmptiesTheList()
    {
        _chat.AddSystem("hello");

        _chat.Clear();

        Assert.Equal(0, _feed.GetChildCount());
    }

    [Fact]
    public void LinesNeverTakeTheMouse()
    {
        _chat.AddSystem("hello");
        _chat.Add(new ChatLine.Roll(1, "Alice", 42));

        Assert.All(_feed.GetChildren(), child =>
            Assert.Equal(Control.MouseFilterEnum.Ignore, ((Control)child).MouseFilter));
    }
}
