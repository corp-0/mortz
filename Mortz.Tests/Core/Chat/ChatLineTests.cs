using Mortz.Core.Chat;
using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core.Chat;

public class ChatLineTests
{
    [Fact]
    public void ChatEntries_KeepPlayerMarkdownSeparateFromPlainAndTrustedText()
    {
        ChatLine.Player player = new(1, "Alice", "**hello** [b]bad[/b]");
        ChatLine.System plain = new("[b]plain[/b]");
        ChatLine.System rich = new(new RichText().Add("trusted", new Style().Bold()));

        Assert.Equal("[b]hello[/b] bad", player.Render().ToString());
        Assert.Equal("[lb]b[rb]plain[lb]/b[rb]", plain.Render().ToString());
        Assert.Equal("[b]trusted[/b]", rich.Render().ToString());
    }

    [Fact]
    public void DomainLinesRejectInvalidValuesAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ChatLine.Player(1, "Alice", " "));
        Assert.Throws<ArgumentException>(() => new ChatLine.Player(1, "", "hello"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChatLine.Player(0, "Alice", "hello"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChatLine.Roll(1, "Alice", 101));
        Assert.Throws<ArgumentException>(() => new ChatLine.System(""));
    }
}
