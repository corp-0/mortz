using Mortz.Server;
using Xunit;

namespace Mortz.Tests.Server;

public class WinTrackerTests
{
    [Fact]
    public void WinsAccumulatePerPeer()
    {
        WinTracker tracker = new();

        tracker.RecordWin(7);
        tracker.RecordWin(7);
        tracker.RecordWin(9);

        Assert.Equal(2, tracker.Wins(7));
        Assert.Equal(1, tracker.Wins(9));
        Assert.Equal(0, tracker.Wins(8));
    }

    [Fact]
    public void LeavingDeletesTheTally()
    {
        WinTracker tracker = new();
        tracker.RecordWin(7);

        tracker.Remove(7);

        Assert.Equal(0, tracker.Wins(7));
    }
}
