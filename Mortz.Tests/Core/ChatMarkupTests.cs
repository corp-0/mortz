using Mortz.Core;
using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core;

public class ChatMarkupTests
{
    [Fact]
    public void Markdown_RendersSupportedInlineStylesAsBbCode()
    {
        RichText rendered = ChatMarkdown.Render(
            "**bold** _italic_ ~~gone~~ `code` [site](https://example.com/a?q=1)");

        Assert.Equal("[b]bold[/b] [i]italic[/i] [s]gone[/s] " +
            "[code]code[/code] [url=https://example.com/a?q=1]site[/url]",
            rendered.ToString());
    }

    [Fact]
    public void Markdown_SupportsNestedStylesAndEscapedMarkers()
    {
        RichText rendered = ChatMarkdown.Render("**bold _and italic_** \\*literal\\*");

        Assert.Equal("[b]bold [i]and italic[/i][/b] *literal*", rendered.ToString());
    }

    [Fact]
    public void Markdown_StripsDirectBbCodeBeforeRendering()
    {
        RichText rendered = ChatMarkdown.Render(
            "[color=red]**hello**[/color] [wave amp=50]there[/wave]");

        Assert.Equal("[b]hello[/b] there", rendered.ToString());
        Assert.True(ChatTextSanitizer.TrySanitize("[b] hello [/b]", out string text,
            out ChatRejectReason reason));
        Assert.Equal("hello", text);
        Assert.Equal(ChatRejectReason.None, reason);
        Assert.False(ChatTextSanitizer.TrySanitize("[b][/b]", out _, out _));
    }

    [Fact]
    public void Markdown_DoesNotCreateUnsafeLinks()
    {
        RichText rendered = ChatMarkdown.Render("[click](javascript:alert(1))");

        Assert.Equal("[lb]click[rb](javascript:alert(1))", rendered.ToString());
    }

    [Fact]
    public void RichText_EscapesValuesAndAppliesFluentStyles()
    {
        RichText rendered = new RichText()
            .Add("prefix [b] ")
            .Bold().Color("#ff00aa").ApplyTo("dynamic [name]");

        Assert.Equal("prefix [lb]b[rb] [color=#ff00aa][b]dynamic " +
            "[lb]name[rb][/b][/color]", rendered.ToString());
    }

    [Fact]
    public void ChatEntries_KeepPlayerMarkdownSeparateFromPlainAndTrustedText()
    {
        ChatEntry player = new(ChatEntryKind.Player, 1, "Alice", "**hello** [b]bad[/b]",
            ChatTextFormat.Markdown);
        ChatEntry plain = new(ChatEntryKind.System, 0, "Server", "[b]plain[/b]");
        ChatState state = new();
        state.AddSystem(new RichText().Bold().ApplyTo("trusted"));

        Assert.Equal("[b]hello[/b] bad", player.Render().ToString());
        Assert.Equal("[lb]b[rb]plain[lb]/b[rb]", plain.Render().ToString());
        Assert.Equal("[b]trusted[/b]", state.Entries[0].Render().ToString());
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#abcd")]
    [InlineData("#abcdef")]
    [InlineData("#abcdef12")]
    public void RichText_AcceptsGodotHexColors(string color)
    {
        Assert.Equal($"[color={color}]x[/color]",
            new RichText().Color(color).ApplyTo("x").ToString());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#xyzxyz")]
    [InlineData("#12345]")]
    public void RichText_RejectsUnsafeColors(string color)
    {
        Assert.Throws<ArgumentException>(() => new RichText().Color(color));
    }
}
