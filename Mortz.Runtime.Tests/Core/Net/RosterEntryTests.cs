using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Roster;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

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
}
