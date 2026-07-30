using Mortz.Core.Match;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class RosterEntryTests
{
    [Fact]
    public void ARosterEntryNeedsAPositivePeerId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(0, "Alice", 0, null, new NetSlot(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(-3, "Alice", 0, null, new NetSlot(1)));
    }

    [Fact]
    public void ARosterEntryKeepsWhatItWasGiven()
    {
        RosterEntry entry = new(7, "Alice", 3, Team.RED, new NetSlot(2));

        Assert.Equal(7, entry.PeerId);
        Assert.Equal("Alice", entry.Name);
        Assert.Equal(3, entry.Skin);
        Assert.Equal(Team.RED, entry.Team);
        Assert.Equal(2, entry.Slot.Value);
    }
}
