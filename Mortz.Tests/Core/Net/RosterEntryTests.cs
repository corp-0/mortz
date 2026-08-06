using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Roster;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class RosterEntryTests
{
    [Fact]
    public void ARosterEntryNeedsAPositivePeerId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(0, "Alice", 0, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(-3, "Alice", 0, null, 1));
    }

    [Fact]
    public void ARosterEntryNeedsAHoldableSlot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(7, "Alice", 0, null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RosterEntry(7, "Alice", 0, null, (byte)(NetConfig.MAX_PLAYERS + 1)));
    }

    [Fact]
    public void ARosterEntryKeepsWhatItWasGiven()
    {
        RosterEntry entry = new(7, "Alice", 3, Team.RED, 2);

        Assert.Equal(7, entry.PeerId);
        Assert.Equal("Alice", entry.Name);
        Assert.Equal(3, entry.Skin);
        Assert.Equal(Team.RED, entry.Team);
        Assert.Equal(2, entry.Slot);
    }
}
