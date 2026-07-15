using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class FirstBloodTrackerTests
{
    [Fact]
    public void UncreditedDeathsDoNotClaimFirstBlood()
    {
        FirstBloodTracker tracker = new();

        Assert.False(tracker.TryClaim(killerKnown: false, suicide: false, teamKill: false));
        Assert.False(tracker.TryClaim(killerKnown: true, suicide: true, teamKill: false));
        Assert.False(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: true));
        Assert.True(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: false));
    }

    [Fact]
    public void OnlyTheFirstCreditedKillClaimsIt()
    {
        FirstBloodTracker tracker = new();

        Assert.True(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: false));
        Assert.False(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: false));
    }

    [Fact]
    public void ResetStartsANewMatch()
    {
        FirstBloodTracker tracker = new();
        Assert.True(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: false));

        tracker.Reset();

        Assert.True(tracker.TryClaim(killerKnown: true, suicide: false, teamKill: false));
    }
}
