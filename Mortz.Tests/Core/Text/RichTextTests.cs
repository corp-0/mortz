using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core.Text;

public class RichTextTests
{
    [Fact]
    public void RichText_EscapesValuesAndAppliesStyles()
    {
        RichText rendered = new RichText()
            .Add("prefix [b] ")
            .Add("dynamic [name]", new Style().Bold().Color("#ff00aa"));

        Assert.Equal("prefix [lb]b[rb] [color=#ff00aa][b]dynamic " +
            "[lb]name[rb][/b][/color]", rendered.ToString());
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#abcd")]
    [InlineData("#abcdef")]
    [InlineData("#abcdef12")]
    public void RichText_AcceptsGodotHexColors(string color)
    {
        Assert.Equal($"[color={color}]x[/color]",
            new RichText().Add("x", new Style().Color(color)).ToString());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#xyzxyz")]
    [InlineData("#12345]")]
    public void RichText_RejectsUnsafeColors(string color)
    {
        Assert.Throws<ArgumentException>(() => new Style().Color(color));
        Assert.Throws<ArgumentException>(() => new Style().Pulse(2f, color, -2f));
    }

    [Fact]
    public void RichText_RendersAnimationTagsWithInvariantNumbers()
    {
        Assert.Equal("[wave amp=24.0 freq=6.0]x[/wave]",
            new RichText().Add("x", new Style().Wave(24f, 6f)).ToString());
        Assert.Equal("[shake rate=20.0 level=12]x[/shake]",
            new RichText().Add("x", new Style().Shake(20f, 12)).ToString());
        Assert.Equal("[tornado radius=6.0 freq=4.0]x[/tornado]",
            new RichText().Add("x", new Style().Tornado(6f, 4f)).ToString());
        Assert.Equal("[pulse freq=2.0 color=#ffffff50 ease=-2.0]x[/pulse]",
            new RichText().Add("x", new Style().Pulse(2f, "#ffffff50", -2f)).ToString());
    }

    [Fact]
    public void RichText_WrapsTrustedRichTextWithoutReescaping()
    {
        RichText inner = new RichText().Add("[name]", new Style().Bold());
        Assert.Equal("[center][wave amp=24.0 freq=6.0][b][lb]name[rb][/b][/wave][/center]",
            new RichText().Add(inner, new Style().Wave(24f, 6f).Center()).ToString());
    }

    [Fact]
    public void RichText_WrapStylesTheWholeBuffer()
    {
        RichText line = new RichText()
            .Add("a [x] ")
            .Add("b", new Style().Bold())
            .Wrap(new Style().Color("#ff00aa").Center());

        Assert.Equal("[center][color=#ff00aa]a [lb]x[rb] [b]b[/b][/color][/center]",
            line.ToString());
    }
}
