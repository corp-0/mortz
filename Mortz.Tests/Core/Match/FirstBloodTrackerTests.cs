using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class FirstBloodTrackerTests
{
    [Fact]
    public void UncreditedDeathsDoNotClaimFirstBlood()
    {
        FirstBloodTracker tracker = new();

        Assert.False(tracker.TryClaim(creditedKill: false));
        Assert.False(tracker.TryClaim(creditedKill: false));
        Assert.False(tracker.TryClaim(creditedKill: false));
        Assert.True(tracker.TryClaim(creditedKill: true));
    }

    [Fact]
    public void OnlyTheFirstCreditedKillClaimsIt()
    {
        FirstBloodTracker tracker = new();

        Assert.True(tracker.TryClaim(creditedKill: true));
        Assert.False(tracker.TryClaim(creditedKill: true));
    }

    [Fact]
    public void ResetStartsANewMatch()
    {
        FirstBloodTracker tracker = new();
        Assert.True(tracker.TryClaim(creditedKill: true));

        tracker.Reset();

        Assert.True(tracker.TryClaim(creditedKill: true));
    }
}
