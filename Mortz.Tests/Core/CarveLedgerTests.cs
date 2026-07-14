using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

/// <summary>
/// Predicted-carve side effects must be rollback-safe. Once a shot settles (its
/// authoritative carve arrives, or a parry cancels it) the ledger must keep
/// suppressing it, so a stale queued impact or a replay re-report can't carve
/// the same shot a second time and leave a hole the server never confirms, which
/// would only revert on the 2 s timeout: a ghost.
/// </summary>
public class CarveLedgerTests
{
    private static readonly List<(int X, int Y)> NoPixels = new();

    [Fact]
    public void SettledShot_StaysSuppressed_SoItIsNotCarvedTwice()
    {
        CarveLedger ledger = new();

        // The first predicted carve for seq 7 is allowed through.
        Assert.False(ledger.IsPending(7) || ledger.IsSettled(7));
        ledger.AddPending(7, 10, 10, 4, NoPixels, now: 0);
        Assert.True(ledger.IsPending(7)); // a duplicate while pending is suppressed

        // The server's carve confirms it: pending clears, shot is now settled.
        Assert.True(ledger.TryConfirm(7, out _));
        ledger.MarkSettled(7, now: 0);
        Assert.False(ledger.IsPending(7));

        // The crux: a stale impact or replay re-report after confirmation must
        // still find the shot handled, so PredictCarve skips it.
        Assert.True(ledger.IsSettled(7));
    }

    [Fact]
    public void SettledMemory_IsBounded_AndForgottenAfterItsWindow()
    {
        CarveLedger ledger = new();
        ledger.MarkSettled(7, now: 0);
        Assert.True(ledger.IsSettled(7));

        // Long after the shot is done (past the replay window), it's forgotten.
        ledger.Expire(now: 10_000);
        Assert.False(ledger.IsSettled(7));
    }
}
