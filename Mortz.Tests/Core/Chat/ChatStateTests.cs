using Mortz.Core.Chat;
using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core.Chat;

public class ChatStateTests
{
    [Fact]
    public void State_PersistsStructuredEntriesAndEvictsOldest()
    {
        ChatState state = new(capacity: 2);
        state.Add(new ChatEntry(ChatEntryKind.PLAYER, 7, "Alice", "one"));
        state.AddSystem("two");
        state.AddSystem("three", isPrivate: true);

        Assert.Equal(2, state.Entries.Count);
        Assert.Equal("two", state.Entries[0].Text);
        Assert.Equal(ChatEntryKind.PRIVATE, state.Entries[1].Kind);
    }

    [Fact]
    public void ChatEntries_KeepPlayerMarkdownSeparateFromPlainAndTrustedText()
    {
        ChatEntry player = new(ChatEntryKind.PLAYER, 1, "Alice", "**hello** [b]bad[/b]",
            ChatTextFormat.MARKDOWN);
        ChatEntry plain = new(ChatEntryKind.SYSTEM, 0, "Server", "[b]plain[/b]");
        ChatState state = new();
        state.AddSystem(new RichText().Bold().ApplyTo("trusted"));

        Assert.Equal("[b]hello[/b] bad", player.Render().ToString());
        Assert.Equal("[lb]b[rb]plain[lb]/b[rb]", plain.Render().ToString());
        Assert.Equal("[b]trusted[/b]", state.Entries[0].Render().ToString());
    }
}
