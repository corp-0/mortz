using Mortz.Core.Text;
using Xunit;

namespace Mortz.Tests.Core.Text;

public class RichTextTests
{
    [Fact]
    public void RichText_EscapesValuesAndAppliesFluentStyles()
    {
        RichText rendered = new RichText()
            .Add("prefix [b] ")
            .Bold().Color("#ff00aa").ApplyTo("dynamic [name]");

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
