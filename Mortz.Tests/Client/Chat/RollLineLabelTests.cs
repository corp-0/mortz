using Mortz.Client.Chat;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client.Chat;

[Collection(nameof(GodotHeadlessCollection))]
public class RollLineLabelTests
{
    [Fact]
    public void LiveRollSpinsThenSettlesOnTheRealValue()
    {
        RollLineLabel label = RollLineLabel.Create("Alice", 73, animate: true);
        try
        {
            Assert.Contains("rolls...", label.Text);

            label._Process(2.0); // one giant tick lands past the spin window

            Assert.Contains("rolled", label.Text);
            Assert.Contains("73", label.Text);
            Assert.Contains("(1-100)", label.Text);
        }
        finally
        {
            label.Free();
        }
    }

    [Fact]
    public void RebuiltHistoryRendersAlreadySettled()
    {
        RollLineLabel label = RollLineLabel.Create("Alice", 73, animate: false);
        try
        {
            Assert.DoesNotContain("rolls...", label.Text);
            Assert.Contains("73", label.Text);
        }
        finally
        {
            label.Free();
        }
    }
}
