using Mortz.Core.Chat;
using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core.Chat;

public class ChatMarkdownTests
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
        Assert.Equal(ChatRejectReason.NONE, reason);
        Assert.False(ChatTextSanitizer.TrySanitize("[b][/b]", out _, out _));
    }

    [Fact]
    public void Markdown_DoesNotCreateUnsafeLinks()
    {
        RichText rendered = ChatMarkdown.Render("[click](javascript:alert(1))");

        Assert.Equal("[lb]click[rb](javascript:alert(1))", rendered.ToString());
    }
}
