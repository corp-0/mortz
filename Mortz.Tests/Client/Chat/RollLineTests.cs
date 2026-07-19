using Godot;
using Mortz.Client.Chat;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(GodotHeadlessCollection))]
public class RollLineTests
{
    [Fact]
    public void LiveRollSpinsThenSettlesOnTheRealValue()
    {
        RollLine line = RollLine.Create("Alice", 73, animate: true);
        try
        {
            RichTextLabel prefix = Assert.IsType<RichTextLabel>(line.GetChild(0));
            Assert.Contains("rolls", prefix.Text);
            Assert.DoesNotContain("rolled", prefix.Text);

            line._Process(60.0); // one giant step lands past the spin window

            RichTextLabel settled = Assert.IsType<RichTextLabel>(line.GetChild(0));
            Assert.Contains("rolled", settled.Text);
            Assert.Contains("73", settled.Text);
            Assert.Contains("(1-100)", settled.Text);
        }
        finally
        {
            line.Free();
        }
    }

    [Fact]
    public void RebuiltHistoryRendersAlreadySettled()
    {
        RollLine line = RollLine.Create("Alice", 73, animate: false);
        try
        {
            RichTextLabel settled = Assert.IsType<RichTextLabel>(line.GetChild(0));
            Assert.DoesNotContain("rolls...", settled.Text);
            Assert.Contains("73", settled.Text);
        }
        finally
        {
            line.Free();
        }
    }
}
