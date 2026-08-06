using Mortz.Client.E2E;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>One physics tick consumes one input sequence, so a plan is pure
/// arithmetic over the sequence it was installed at.</summary>
public class BotInputScriptTests
{
    private static BotInputScript Script(int firstSequence) => new(
        BotInputPlan.Sequence(
            new BotInputFrame(InputButtons.LEFT, aim: 10, ticks: 3),
            new BotInputFrame(InputButtons.FIRE, aim: 64, ticks: 2)),
        firstSequence);

    [Fact]
    public void TheLastSequenceIsTheFirstPlusEveryTickMinusOne()
    {
        BotInputScript script = Script(100);

        Assert.Equal(100, script.FirstSequence);
        Assert.Equal(104, script.LastSequence);
    }

    [Fact]
    public void FramesOwnConsecutiveSequenceRuns()
    {
        BotInputScript script = Script(100);

        Assert.Equal(InputButtons.LEFT, script.ButtonsAt(100));
        Assert.Equal(InputButtons.LEFT, script.ButtonsAt(102));
        Assert.Equal(InputButtons.FIRE, script.ButtonsAt(103));
        Assert.Equal(InputButtons.FIRE, script.ButtonsAt(104));
    }

    [Fact]
    public void AimFollowsTheSameRuns()
    {
        BotInputScript script = Script(100);

        Assert.Equal(10, script.AimAt(102));
        Assert.Equal(64, script.AimAt(103));
    }

    [Fact]
    public void DoneOnlyPastTheLastSequence()
    {
        BotInputScript script = Script(100);

        Assert.False(script.IsDone(104));
        Assert.True(script.IsDone(105));
    }

    [Fact]
    public void InstallSequenceShiftsTheWholePlan()
    {
        BotInputScript script = Script(0);

        Assert.Equal(0, script.FirstSequence);
        Assert.Equal(4, script.LastSequence);
        Assert.Equal(InputButtons.FIRE, script.ButtonsAt(3));
    }

    [Fact]
    public void ASingleFramePlanCoversExactlyItsTicks()
    {
        BotInputScript script = new(BotInputPlan.Wait(1), firstSequence: 7);

        Assert.Equal(7, script.LastSequence);
        Assert.False(script.IsDone(7));
        Assert.True(script.IsDone(8));
    }

    [Fact]
    public void ThePlanIdIsCarriedThrough()
    {
        BotInputPlan plan = BotInputPlan.Hold(InputButtons.PARRY, ticks: 5);

        BotInputScript script = new(plan, firstSequence: 1);

        Assert.Equal(plan.Id, script.PlanId);
    }
}
