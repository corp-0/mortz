using Mortz.Client.Announcements;
using Xunit;

namespace Mortz.Tests.Client.Announcements;

public class VariantPickerTests
{
    [Fact]
    public void ASingleVariantIsAlwaysPicked()
    {
        VariantPicker picker = new(new Random(1));

        Assert.All(Enumerable.Range(0, 20), _ => Assert.Equal(0, picker.Next("cue", 1)));
    }

    [Fact]
    public void NeverTheSameVariantTwiceInARow()
    {
        VariantPicker picker = new(new Random(1));

        int last = picker.Next("cue", 3);
        for (int i = 0; i < 200; i++)
        {
            int next = picker.Next("cue", 3);
            Assert.NotEqual(last, next);
            last = next;
        }
    }

    [Fact]
    public void EveryVariantIsEventuallyPicked()
    {
        VariantPicker picker = new(new Random(1));

        HashSet<int> seen = new();
        for (int i = 0; i < 200; i++)
        {
            seen.Add(picker.Next("cue", 3));
        }

        Assert.Equal([0, 1, 2], seen.Order());
    }

    [Fact]
    public void KeysDoNotShareTheirNoRepeatMemory()
    {
        VariantPicker picker = new(new Random(1));

        // A pick on one key must not constrain another key's pool of 1.
        picker.Next("loud", 2);
        Assert.Equal(0, picker.Next("quiet", 1));
    }
}
